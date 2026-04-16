using Basta.Server.DTOs;

namespace Basta.Server.Services;

/// <summary>
/// Application-level service that orchestrates the full game creation workflow,
/// including persistence and in-memory session initialization.
/// </summary>
public interface IGameOperationService
{
    Task<CreateGameResult> CreateGameAsync(
        string nickname,
        string preferredLanguage,
        int totalRounds,
        int timerDuration,
        List<int> categoryIds,
        CancellationToken cancellationToken = default);

    Task<JoinGameResult> JoinGameAsync(
        string code,
        string nickname,
        string preferredLanguage,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a successful game creation operation.
/// </summary>
public record CreateGameResult(string GameCode, int HostUserId);

/// <summary>
/// Represents the result of a successful join operation.
/// </summary>
public record JoinGameResult(int UserId, string GameCode);
