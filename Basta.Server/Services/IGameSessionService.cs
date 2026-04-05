using Basta.Server.Models;

namespace Basta.Server.Services;

public interface IGameSessionService
{
    string CreateSession(int hostUserId, int totalRounds, int timerDuration);
    GameSession? TryGetSession(string code);
}
