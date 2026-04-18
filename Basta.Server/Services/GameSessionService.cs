using System.Collections.Concurrent;
using Basta.Server.Models;
using Basta.Server.DTOs;

namespace Basta.Server.Services;

public class GameSessionService : IGameSessionService
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public string CreateSession(int hostUserId, int totalRounds, int timerDuration, List<int> categoryIds)
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
                TimerDuration = timerDuration,
                SelectedCategoryIds = categoryIds
            };
        } while (!_sessions.TryAdd(code, session));

        return code;
    }

    public GameSession? TryGetSession(string code)
    {
        _sessions.TryGetValue(code, out var session);
        return session;
    }

    public bool RemoveSession(string code)
    {
        return _sessions.TryRemove(code, out _);
    }

    public bool TryAddPlayer(string code, int userId, string nickname, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!_sessions.TryGetValue(code, out var session))
        {
            errorMessage = "Game session not found.";
            return false;
        }

        lock (session)
        {
            if (session.Players.Count >= 5 && !session.Players.ContainsKey(userId))
            {
                errorMessage = "Game session is full.";
                return false;
            }

            // Reject duplicate nicknames from *different* users
            if (session.Players.Any(kvp => kvp.Key != userId &&
                    string.Equals(kvp.Value, nickname, StringComparison.OrdinalIgnoreCase)))
            {
                errorMessage = "Nickname is already taken in this session.";
                return false;
            }

            // Also prevents duplicate additions since it's a Dictionary
            session.Players[userId] = nickname;
        }

        return true;
    }

    public void RemovePlayer(string code, int userId)
    {
        if (_sessions.TryGetValue(code, out var session))
        {
            lock (session)
            {
                session.Players.Remove(userId);
            }
        }
    }

    public LobbySnapshot? GetLobbySnapshot(string code)
    {
        if (!_sessions.TryGetValue(code, out var session))
        {
            return null;
        }

        lock (session)
        {
            var players = session.Players.Select(kvp => new LobbyPlayer
            {
                UserId = kvp.Key,
                Nickname = kvp.Value,
                Score = 0, // For now, score is 0 in the lobby
                IsHost = kvp.Key == session.HostUserId,
                IsOnline = true
            }).ToList();

            return new LobbySnapshot
            {
                GameCode = session.Code,
                HostUserId = session.HostUserId,
                TotalRounds = session.TotalRounds,
                TimerDuration = session.TimerDuration,
                Language = string.Empty,
                State = "Lobby",
                Players = players
            };
        }
    }

    private static string GenerateCode()
    {
        // Excludes visually ambiguous characters: I, O, Q, V, Z
        const string chars = "ABCDEFGHJKLMNPRSTUWXY";
        return new string(Enumerable.Repeat(chars, 4)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }
}
