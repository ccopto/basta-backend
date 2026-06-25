using Basta.Server.Data;
using Basta.Server.DTOs;
using Basta.Server.Entities;
using Microsoft.EntityFrameworkCore;


namespace Basta.Server.Services;

/// <summary>
/// Orchestrates the game creation workflow:
/// 1. Persists the host User and gets their generated UserId.
/// 2. Registers an in-memory GameSession via IGameSessionService.
/// 3. Persists the Game and GamePlayer entities.
/// All DB operations are wrapped in a transaction to guarantee atomicity.
/// If the transaction fails after the in-memory session was registered, the
/// session is removed to keep in-memory and DB state consistent.
/// </summary>
public class GameOperationService : IGameOperationService
{
    private readonly BastaDbContext _context;
    private readonly IGameSessionService _gameSessionService;
    private readonly IDictionaryService _dictionaryService;

    public GameOperationService(
        BastaDbContext context,
        IGameSessionService gameSessionService,
        IDictionaryService dictionaryService)
    {
        _context = context;
        _gameSessionService = gameSessionService;
        _dictionaryService = dictionaryService;
    }

    public async Task<CreateGameResult> CreateGameAsync(
        string nickname,
        string preferredLanguage,
        string language,
        int totalRounds,
        int timerDuration,
        List<int> categoryIds,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Track the session code outside the try so the catch block can clean it up.
        string? sessionCode = null;

        try
        {
            // 1. Create the host User and get their generated UserId
            var host = new User
            {
                Nickname = nickname,
                PreferredLanguage = preferredLanguage
            };

            _context.Users.Add(host);
            await _context.SaveChangesAsync(cancellationToken);

            // 2. Register the in-memory session (after UserId is available)
            sessionCode = _gameSessionService.CreateSession(host.UserId, nickname, totalRounds, timerDuration, categoryIds, preferredLanguage);

            // 3. Persist the Game entity
            var game = new Game
            {
                GameId = sessionCode,
                HostUserId = host.UserId,
                TotalRounds = totalRounds,
                TimerDuration = timerDuration,
                Language = language
            };
            _context.Games.Add(game);

            // 4. Add the host as the first GamePlayer
            var gamePlayer = new GamePlayer
            {
                GameId = sessionCode,
                UserId = host.UserId
            };
            _context.GamePlayers.Add(gamePlayer);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new CreateGameResult(sessionCode, host.UserId);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);

            // If the in-memory session was already registered before the DB failure,
            // remove it so the two stores stay consistent.
            if (sessionCode is not null)
            {
                _gameSessionService.RemoveSession(sessionCode);
            }

            throw;
        }
    }

    public async Task<JoinGameResult> JoinGameAsync(
        string code,
        string nickname,
        string preferredLanguage,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        int? generatedUserId = null;

        try
        {
            // 1. Create the joining User and get their generated UserId
            var user = new User
            {
                Nickname = nickname,
                PreferredLanguage = preferredLanguage
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync(cancellationToken);
            generatedUserId = user.UserId;

            // 2. Validate joining capability in the session memory
            if (!_gameSessionService.TryAddPlayer(code, user.UserId, nickname, out var errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }

            // 3. Persist the GamePlayer mapping for cumulative scores reporting
            var gamePlayer = new GamePlayer
            {
                GameId = code,
                UserId = user.UserId
            };
            
            _context.GamePlayers.Add(gamePlayer);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new JoinGameResult(user.UserId, code);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);

            if (generatedUserId.HasValue)
            {
                // Defensive call for when DB transactions fail after in-memory registration.
                // If TryAddPlayer caused the failure, this is safely a no-op.
                _gameSessionService.RemovePlayer(code, generatedUserId.Value);
            }
            
            throw;
        }
    }

    public async Task SubmitAnswersAsync(
        string code,
        int roundNumber,
        int userId,
        Dictionary<int, string> answers,
        CancellationToken cancellationToken = default)
    {
        // 1. Fetch the game language and category validation types
        var game = await _context.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.GameId == code, cancellationToken);
        var language = game?.Language ?? "en";

        var categoryTypes = await _context.Categories
            .AsNoTracking()
            .Where(c => answers.Keys.Contains(c.CategoryId))
            .ToDictionaryAsync(c => c.CategoryId, c => c.ValidationType, cancellationToken);

        // 2. Create RoundAnswer entities, running Phase 1 dictionary check for each
        var roundAnswers = answers.Select(kvp =>
        {
            var answer = kvp.Value?.Trim() ?? string.Empty;
            var validationType = categoryTypes.TryGetValue(kvp.Key, out var vt) ? vt : CategoryValidationType.CommonWord;

            var dictValid = !string.IsNullOrWhiteSpace(answer)
                && answer.Length >= 2
                && _dictionaryService.IsValidWord(answer, language, validationType);

            return new RoundAnswer
            {
                GameId = code,
                RoundNumber = roundNumber,
                UserId = userId,
                CategoryId = kvp.Key,
                SubmittedAnswer = answer,
                DictionaryValid = dictValid,
                RequiresPeerReview = !dictValid,
                // Phase 1 pass: auto-accept. Phase 1 fail: leave null (peer decides).
                IsValid = dictValid ? true : null,
                PointsAwarded = 0
            };
        }).ToList();

        // 3. Persist to DB
        _context.RoundAnswers.AddRange(roundAnswers);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateValidationAsync(
        string gameId, 
        int roundNumber, 
        int userId, 
        Dictionary<int, bool> validations,
        CancellationToken cancellationToken = default)
    {
        // 1. Fetch all answers for this player in this round
        var answers = await _context.RoundAnswers
            .Where(a => a.GameId == gameId && a.RoundNumber == roundNumber && a.UserId == userId)
            .ToListAsync(cancellationToken);

        // 2. Update the IsValid flag for each one
        foreach (var answer in answers)
        {
            if (validations.TryGetValue(answer.CategoryId, out var isValid))
            {
                answer.IsValid = isValid;
            }
        }

        // 3. Persist changes
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<RoundAnswersDto> GetRoundAnswersDtoAsync(
        string gameId,
        int roundNumber,
        CancellationToken cancellationToken = default)
    {
        var roundAnswers = await _context.RoundAnswers
            .Where(a => a.GameId == gameId && a.RoundNumber == roundNumber)
            .ToListAsync(cancellationToken);

        var gamePlayers = await _context.GamePlayers
            .Include(gp => gp.User)
            .Where(gp => gp.GameId == gameId)
            .ToListAsync(cancellationToken);

        var playerDtos = gamePlayers.Select(gp =>
        {
            var playerAnswers = roundAnswers
                .Where(a => a.UserId == gp.UserId)
                .Select(a => new AnswerValidationDto(
                    a.RoundAnswerId,
                    a.CategoryId,
                    a.SubmittedAnswer,
                    a.DictionaryValid,
                    a.RequiresPeerReview))
                .ToList();

            return new PlayerAnswersDto(
                gp.UserId,
                gp.User?.Nickname ?? "Unknown",
                playerAnswers);
        }).ToList();

        return new RoundAnswersDto(playerDtos);
    }

    public async Task<bool> ValidatePlayerAsync(string code, int userId, CancellationToken cancellationToken = default)
    {
        return await _context.GamePlayers.AnyAsync(gp => gp.GameId == code && gp.UserId == userId, cancellationToken);
    }
}
