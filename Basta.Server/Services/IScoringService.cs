using Basta.Server.DTOs;

namespace Basta.Server.Services;

public interface IScoringService
{
    /// <summary>
    /// Calculates points for the given round based on rules (unique: 10, shared: 5, invalid: 0).
    /// Awards points in the DB and updates cumulative scores.
    /// </summary>
    Task<List<PlayerScoreDto>> CalculateAndAwardPointsAsync(string gameId, int roundNumber, char roundLetter);

    /// <summary>
    /// Fetches all players for a game and ranks them by cumulative score.
    /// </summary>
    Task<LeaderboardDto> GetLeaderboardAsync(string gameId, string reason);
}
