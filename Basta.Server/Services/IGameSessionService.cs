using Basta.Server.Models;

namespace Basta.Server.Services;

public interface IGameSessionService
{
    string CreateSession(int hostUserId, int totalRounds, int timerDuration);
    GameSession? TryGetSession(string code);

    /// <summary>
    /// Removes an in-memory session by code. Used to roll back a session that was
    /// registered before a database transaction failed.
    /// </summary>
    bool RemoveSession(string code);
}
