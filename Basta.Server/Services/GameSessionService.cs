using System.Collections.Concurrent;
using Basta.Server.Models;

namespace Basta.Server.Services;

public class GameSessionService : IGameSessionService
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public string CreateSession(int hostUserId, int totalRounds, int timerDuration)
    {
        // Atomic loop: TryAdd returns false if the code already exists, so we keep
        // generating until we win the insert. This eliminates the TOCTOU window that
        // existed between ContainsKey and TryAdd.
        string code;
        GameSession session;
        do
        {
            code = GenerateCode();
            session = new GameSession
            {
                Code = code,
                HostUserId = hostUserId,
                TotalRounds = totalRounds,
                TimerDuration = timerDuration
            };
        } while (!_sessions.TryAdd(code, session));

        return code;
    }

    public GameSession? TryGetSession(string code)
    {
        _sessions.TryGetValue(code, out var session);
        return session;
    }

    /// <inheritdoc/>
    public bool RemoveSession(string code)
    {
        return _sessions.TryRemove(code, out _);
    }

    private static string GenerateCode()
    {
        // Excludes visually ambiguous characters: I, O, Q, V, Z
        const string chars = "ABCDEFGHJKLMNPRSTUWXY";
        return new string(Enumerable.Repeat(chars, 4)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }
}
