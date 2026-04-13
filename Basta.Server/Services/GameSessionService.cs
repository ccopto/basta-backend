using System.Collections.Concurrent;
using Basta.Server.Models;

namespace Basta.Server.Services;

public class GameSessionService : IGameSessionService
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public string CreateSession(int hostUserId, int totalRounds, int timerDuration)
    {
        string code;
        do
        {
            code = GenerateCode();
        } while (_sessions.ContainsKey(code));

        var session = new GameSession
        {
            Code = code,
            HostUserId = hostUserId,
            TotalRounds = totalRounds,
            TimerDuration = timerDuration
        };

        _sessions.TryAdd(code, session);
        return code;
    }

    public GameSession? TryGetSession(string code)
    {
        _sessions.TryGetValue(code, out var session);
        return session;
    }

    private string GenerateCode()
    {
        // Excludes visually ambiguous characters: I, O, Q, V, Z
        const string chars = "ABCDEFGHJKLMNPRSTUWXY";
        return new string(Enumerable.Repeat(chars, 4)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }
}
