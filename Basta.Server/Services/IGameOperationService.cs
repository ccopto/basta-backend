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
        string language,
        int totalRounds,
        int timerDuration,
        List<int> categoryIds,
        CancellationToken cancellationToken = default);

    Task<JoinGameResult> JoinGameAsync(
        string code,
        string nickname,
        string preferredLanguage,
        CancellationToken cancellationToken = default);

    Task SubmitAnswersAsync(
        string code,
        int roundNumber,
        int userId,
        Dictionary<int, string> answers,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists validation results (IsValid flag) for a player's answers in a round.
    /// </summary>
    Task UpdateValidationAsync(
        string gameId, 
        int roundNumber, 
        int userId, 
        Dictionary<int, bool> validations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the answers for all players in a round as a DTO with dictionary validation metadata,
    /// suitable for broadcasting as the DisplayScoring payload.
    /// </summary>
    Task<RoundAnswersDto> GetRoundAnswersDtoAsync(
        string gameId,
        int roundNumber,
        CancellationToken cancellationToken = default);

    Task<bool> ValidatePlayerAsync(string code, int userId, CancellationToken cancellationToken = default);

    Task UpdateGameSettingsAsync(string code, int totalRounds, int timerDuration, string language, CancellationToken cancellationToken = default);

    Task<bool> HasSubmittedAsync(string code, int roundNumber, int userId, CancellationToken cancellationToken = default);
}


/// <summary>
/// Represents the result of a successful game creation operation.
/// </summary>
public record CreateGameResult(string GameCode, int HostUserId);

/// <summary>
/// Represents the result of a successful join operation.
/// </summary>
public record JoinGameResult(int UserId, string GameCode);
