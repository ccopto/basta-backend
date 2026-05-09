using System.Collections.Concurrent;
using Basta.Server.Models;
using Basta.Server.DTOs;

namespace Basta.Server.Services;

public class GameSessionService : IGameSessionService
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _roundTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<GameSessionService> _logger;
    private readonly TimeProvider _timeProvider;

    public GameSessionService(ILogger<GameSessionService> logger, TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string CreateSession(int hostUserId, string hostNickname, int totalRounds, int timerDuration, List<int> categoryIds, string language = "en")
    {
        // Normalize language to "es" or "en", fallback to "en"
        language = string.Equals(language, "es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";

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
                SelectedCategoryIds = categoryIds,
                Language = language
            };
            session.Players[hostUserId] = hostNickname;
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

    public (char? letter, CancellationToken cancellationToken, string? gameOverReason) StartNextRound(string code)
    {
        if (!_sessions.TryGetValue(code, out var session)) 
            return (null, CancellationToken.None, "Session not found.");

        lock (session)
        {
            // 1. Check if we have already completed the intended number of rounds
            if (session.CurrentRound >= session.TotalRounds)
            {
                return (null, CancellationToken.None, "All rounds completed.");
            }

            const string alphabet = "ABCDEFGHJKLMNPRSTUWXY";
            var availableLetters = alphabet.Where(c => !session.UsedLetters.Contains(c)).ToList();

            if (availableLetters.Count == 0) 
                return (null, CancellationToken.None, "No more letters available.");

            var selectedLetter = availableLetters[Random.Shared.Next(availableLetters.Count)];
            
            session.CurrentRound++;
            session.CurrentLetter = selectedLetter;
            session.UsedLetters.Add(selectedLetter);
            session.RoundActive = true;
            session.RoundLocked = false;
            session.CurrentRoundAnswers.Clear();
            session.PlayersValidated.Clear();

            
            // Create and store the Round CTS
            var cts = new CancellationTokenSource();
            _roundTimers.AddOrUpdate(code, cts, (_, old) => {
                old.Cancel();
                old.Dispose();
                return cts;
            });
            
            return (selectedLetter, cts.Token, null);
        }
    }

    public void LockRound(string code)
    {
        if (_sessions.TryGetValue(code, out var session))
        {
            lock (session)
            {
                session.RoundLocked = true;
                session.RoundLockedAt = _timeProvider.GetUtcNow();
                session.RoundActive = false;
                session.PlayersValidated.Clear();
                
                // Cancel and dispose the CTS from our internal tracking
                if (_roundTimers.TryRemove(code, out var cts))
                {
                    try {
                        cts.Cancel();
                        cts.Dispose();
                    } catch { /* Suppress disposal/cancellation errors */ }
                }
            }
        }
    }

    public bool TrySubmitAnswers(string code, int userId, Dictionary<int, string> answers)
    {
        if (!_sessions.TryGetValue(code, out var session)) return false;

        lock (session)
        {
            // If the round is locked, we only allow a 3-second grace period for in-flight submissions
            if (session.RoundLocked)
            {
                if (session.RoundLockedAt == null)
                {
                    _logger.LogWarning("Round is locked but RoundLockedAt is null for session {Code}. Rejecting submission.", code);
                    return false;
                }

                var gracePeriod = TimeSpan.FromSeconds(3);
                if (_timeProvider.GetUtcNow() - session.RoundLockedAt > gracePeriod)
                {
                    return false;
                }
            }

            // Record the answers for this user
            session.CurrentRoundAnswers[userId] = answers;
            return true;
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
                Language = session.Language,
                State = "Lobby",
                Players = players,
                SelectedCategoryIds = session.SelectedCategoryIds
            };
        }
    }

    public const int MinRounds = 1;
    public const int MaxRounds = 20;
    public const int MinTimer = 30;
    public const int MaxTimer = 120;

    public void UpdateSessionSettings(string code, int totalRounds, int timerDuration, List<int> categoryIds)
    {
        if (categoryIds == null || !categoryIds.Any())
        {
            throw new ArgumentException("At least one category must be selected.", nameof(categoryIds));
        }

        if (totalRounds < MinRounds || totalRounds > MaxRounds)
        {
            throw new ArgumentOutOfRangeException(nameof(totalRounds), $"Total rounds must be between {MinRounds} and {MaxRounds}.");
        }

        if (timerDuration < MinTimer || timerDuration > MaxTimer)
        {
            throw new ArgumentOutOfRangeException(nameof(timerDuration), $"Timer duration must be between {MinTimer} and {MaxTimer} seconds.");
        }

        if (_sessions.TryGetValue(code, out var session))
        {
            lock (session)
            {
                session.TotalRounds = totalRounds;
                session.TimerDuration = timerDuration;
                session.SelectedCategoryIds = categoryIds;
            }
        }
    }

    public bool CheckAllAnswersSubmitted(string code)
    {
        if (!_sessions.TryGetValue(code, out var session)) return false;
        lock (session)
        {
            return session.CurrentRoundAnswers.Count >= session.Players.Count;
        }
    }

    public RoundAnswersDto GetCurrentRoundAnswers(string code)
    {
        if (!_sessions.TryGetValue(code, out var session)) return new RoundAnswersDto(new());
        lock (session)
        {
            var players = session.Players.Select(p => new PlayerAnswersDto(
                p.Key,
                p.Value,
                session.CurrentRoundAnswers.GetValueOrDefault(p.Key, new())
            )).ToList();

            return new RoundAnswersDto(players);
        }
    }

    public bool SubmitValidation(string code, int userId)
    {
        if (!_sessions.TryGetValue(code, out var session)) return false;
        lock (session)
        {
            session.PlayersValidated.Add(userId);
            return session.PlayersValidated.Count >= session.Players.Count;
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
