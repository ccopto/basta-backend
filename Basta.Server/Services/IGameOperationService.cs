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
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a successful game creation operation.
/// </summary>
public record CreateGameResult(string GameCode, int HostUserId);
