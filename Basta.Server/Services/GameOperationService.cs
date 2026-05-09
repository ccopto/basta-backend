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

    public GameOperationService(BastaDbContext context, IGameSessionService gameSessionService)
    {
        _context = context;
        _gameSessionService = gameSessionService;
    }

    public async Task<CreateGameResult> CreateGameAsync(
        string nickname,
        string preferredLanguage,
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
                TimerDuration = timerDuration
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
        // 1. Create RoundAnswer entities for each submitted category
        var roundAnswers = answers.Select(kvp => new RoundAnswer
        {
            GameId = code,
            RoundNumber = roundNumber,
            UserId = userId,
            CategoryId = kvp.Key,
            SubmittedAnswer = kvp.Value?.Trim() ?? string.Empty,
            IsValid = null, // To be scored later
            PointsAwarded = 0
        }).ToList();

        // 2. Persist to DB
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
}

