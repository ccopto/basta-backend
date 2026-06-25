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

    public bool IsRoundAcceptingAnswers(string code)
    {
        if (!_sessions.TryGetValue(code, out var session)) return false;

        lock (session)
        {
            if (!session.RoundActive && !session.RoundLocked)
            {
                return false;
            }

            if (session.RoundLocked)
            {
                if (session.RoundLockedAt == null)
                {
                    return false;
                }

                var gracePeriod = TimeSpan.FromSeconds(3);
                if (_timeProvider.GetUtcNow() - session.RoundLockedAt > gracePeriod)
                {
                    return false;
                }
            }

            return true;
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
                IsOnline = !session.OfflinePlayers.Contains(kvp.Key)
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

    public void ValidateSessionSettings(int totalRounds, int timerDuration, List<int> categoryIds)
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
    }

    public void UpdateSessionSettings(string code, int totalRounds, int timerDuration, List<int> categoryIds, string language)
    {
        ValidateSessionSettings(totalRounds, timerDuration, categoryIds);

        if (_sessions.TryGetValue(code, out var session))
        {
            lock (session)
            {
                session.TotalRounds = totalRounds;
                session.TimerDuration = timerDuration;
                session.SelectedCategoryIds = categoryIds;
                session.Language = string.Equals(language, "es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
            }
        }
    }

    public bool CheckAllAnswersSubmitted(string code)
    {
        if (!_sessions.TryGetValue(code, out var session)) return false;
        lock (session)
        {
            var activePlayerCount = Math.Max(0, session.Players.Count - session.OfflinePlayers.Count);
            return session.CurrentRoundAnswers.Count >= activePlayerCount;
        }
    }

    public RoundAnswersDto GetCurrentRoundAnswers(string code)
    {
        if (!_sessions.TryGetValue(code, out var session)) return new RoundAnswersDto(new());
        lock (session)
        {
            var players = session.Players.Select(p =>
            {
                var rawAnswers = session.CurrentRoundAnswers.GetValueOrDefault(p.Key, new());
                // Convert in-memory raw answers to AnswerValidationDto.
                // DictionaryValid/RequiresPeerReview are not known yet in-memory;
                // the Hub uses GetRoundAnswersDtoAsync (DB-backed) for the actual broadcast.
                var answerDtos = rawAnswers.Select(kvp => new AnswerValidationDto(
                    0, // answerId unknown in-memory
                    kvp.Key,
                    kvp.Value,
                    null, // DictionaryValid unknown until DB persisted
                    true  // Assume peer review needed until DB check
                )).ToList();

                return new PlayerAnswersDto(p.Key, p.Value, answerDtos);
            }).ToList();

            return new RoundAnswersDto(players);
        }
    }

    public bool SubmitValidation(string code, int userId)
    {
        if (!_sessions.TryGetValue(code, out var session)) return false;
        lock (session)
        {
            if (session.PlayersValidated.Contains(userId)) return false;
            session.PlayersValidated.Add(userId);
            var activePlayerCount = Math.Max(0, session.Players.Count - session.OfflinePlayers.Count);
            return session.PlayersValidated.Count >= activePlayerCount;
        }
    }

    public void MarkPlayerOffline(string code, int userId)
    {
        if (_sessions.TryGetValue(code, out var session))
        {
            lock (session)
            {
                session.OfflinePlayers.Add(userId);
            }
        }
    }

    public void MarkPlayerOnline(string code, int userId)
    {
        if (_sessions.TryGetValue(code, out var session))
        {
            lock (session)
            {
                session.OfflinePlayers.Remove(userId);
            }
        }
    }

    public (bool answersQuorumMet, bool validationQuorumMet) MarkPlayerOfflineAndCheckQuorum(string code, int userId)
    {
        if (!_sessions.TryGetValue(code, out var session)) return (false, false);
        lock (session)
        {
            if (session.CurrentRound == 0)
            {
                session.OfflinePlayers.Add(userId);
                return (false, false);
            }

            bool alreadyOffline = session.OfflinePlayers.Contains(userId);

            int beforeOfflineCount = session.OfflinePlayers.Count;
            int beforeActivePlayers = Math.Max(0, session.Players.Count - beforeOfflineCount);

            bool wasAnswersMet = session.CurrentRoundAnswers.Count >= beforeActivePlayers;
            bool wasValidationMet = session.PlayersValidated.Count >= beforeActivePlayers;

            session.OfflinePlayers.Add(userId);

            int afterOfflineCount = session.OfflinePlayers.Count;
            int afterActivePlayers = Math.Max(0, session.Players.Count - afterOfflineCount);

            bool isAnswersMet = session.CurrentRoundAnswers.Count >= afterActivePlayers;
            bool isValidationMet = session.PlayersValidated.Count >= afterActivePlayers;

            bool answersQuorumTipped = session.PlayersValidated.Count == 0 && !alreadyOffline && !wasAnswersMet && isAnswersMet;
            bool validationQuorumTipped = !alreadyOffline && !wasValidationMet && isValidationMet;

            return (answersQuorumTipped, validationQuorumTipped);
        }
    }

    public bool TryRemoveIfStillOffline(string code, int userId)
    {
        if (!_sessions.TryGetValue(code, out var session)) return false;
        lock (session)
        {
            if (session.OfflinePlayers.Contains(userId))
            {
                session.OfflinePlayers.Remove(userId);
                session.Players.Remove(userId);
                return true;
            }
            return false;
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
