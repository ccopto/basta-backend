using Basta.Server.Data;
using Basta.Server.DTOs;
using Basta.Server.Entities;

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
            sessionCode = _gameSessionService.CreateSession(host.UserId, totalRounds, timerDuration);

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
}
