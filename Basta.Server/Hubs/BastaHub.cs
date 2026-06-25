using Microsoft.AspNetCore.SignalR;

using Basta.Server.DTOs;
using Basta.Server.Services;

namespace Basta.Server.Hubs;

public class BastaHub : Hub
{
    private readonly IGameSessionService _gameSessionService;
    private readonly IGameOperationService _gameOperationService;
    private readonly IScoringService _scoringService;
    private readonly IHubContext<BastaHub> _hubContext;
    private readonly ILogger<BastaHub> _logger;

    public BastaHub(
        IGameSessionService gameSessionService, 
        IGameOperationService gameOperationService,
        IScoringService scoringService,
        IHubContext<BastaHub> hubContext,
        ILogger<BastaHub> logger)
    {
        _gameSessionService = gameSessionService;
        _gameOperationService = gameOperationService;
        _scoringService = scoringService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task JoinGame(string code, int userId, string nickname)
    {
        code = code.ToUpperInvariant();
        
        var isValid = await _gameOperationService.ValidatePlayerAsync(code, userId);
        if (!isValid)
        {
            _logger.LogWarning("Player {UserId} ({Nickname}) is not registered for game {Code}.", userId, nickname, code);
            throw new HubException("Player is not registered for this game session.");
        }

        // 1. Add connection to the SignalR group for the specific game code.
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        // 2. Track userId mapped to this connection for disconnection handling later
        Context.Items["UserId"] = userId;
        Context.Items["GameCode"] = code;

        _gameSessionService.MarkPlayerOnline(code, userId);

        // Defensively ensure the player is in the in-memory session.
        // This is idempotent — if the REST /join already registered them,
        // TryAddPlayer is a no-op (returns false with "already registered").
        var added = _gameSessionService.TryAddPlayer(code, userId, nickname, out var errorMessage);
        
        if (!added && errorMessage != "Already registered in this game")
        {
            _logger.LogWarning("Player {UserId} ({Nickname}) joined group {Code} but failed to join session: {Error}", userId, nickname, code, errorMessage);
            throw new HubException(errorMessage);
        }

        // 3. Broadcast the updated lobby snapshot
        var snapshot = _gameSessionService.GetLobbySnapshot(code);
        if (snapshot != null)
        {
            await Clients.Group(code).SendAsync("ReceiveLobbyUpdate", snapshot);
        }
    }

    public async Task StartGame()
    {
        if (Context.Items.TryGetValue("GameCode", out var codeObj) && codeObj is string code &&
            Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int userId)
        {
            var session = _gameSessionService.TryGetSession(code);
            if (session != null)
            {
                // Validate host and player count
                if (session.HostUserId == userId && session.Players.Count >= 2)
                {
                    if (session.SelectedCategoryIds.Count >= 1)
                    {
                        // 1. Broadcast game start to all players in the room
                        await Clients.Group(code).SendAsync("GameStarted");

                        // 2. Automatically trigger the first round
                        await StartRoundInternal(code);
                    }
                    else
                    {
                        await Clients.Caller.SendAsync("Error", "You must select at least one category before starting the game.");
                    }
                }
                else if (session.HostUserId != userId)
                {
                    await Clients.Caller.SendAsync("Error", "Only the host can start the game.");
                }
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "Game session not found.");
            }
        }
        else
        {
            await Clients.Caller.SendAsync("Error", "You must join a game first.");
        }
    }

    public async Task StartRound()
    {
        if (Context.Items.TryGetValue("GameCode", out var codeObj) && codeObj is string code &&
            Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int userId)
        {
            var session = _gameSessionService.TryGetSession(code);
            if (session != null && session.HostUserId == userId)
            {
                await StartRoundInternal(code);
            }
        }
    }

    public async Task CallBasta()
    {
        if (Context.Items.TryGetValue("GameCode", out var codeObj) && codeObj is string code &&
            Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int userId)
        {
            var session = _gameSessionService.TryGetSession(code);
            if (session != null && session.RoundActive && !session.RoundLocked)
            {
                var nickname = session.Players.GetValueOrDefault(userId, "Someone");
                await LockRoundInternal(code, nickname);
            }
        }
    }

    public async Task SubmitAnswers(Dictionary<int, string> answers)
    {
        if (Context.Items.TryGetValue("GameCode", out var codeObj) && codeObj is string code &&
            Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int userId)
        {
            var session = _gameSessionService.TryGetSession(code);
            if (session != null)
            {
                // 1. Check if the round is active or in grace period
                if (!_gameSessionService.IsRoundAcceptingAnswers(code))
                {
                    return;
                }

                try
                {
                    // 2. Persist to DB first and run Phase 1 dictionary validation
                    await _gameOperationService.SubmitAnswersAsync(code, session.CurrentRound, userId, answers);
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                {
                    // Catch DbUpdateException. Check if duplicate via HasSubmittedAsync.
                    var isDuplicate = await _gameOperationService.HasSubmittedAsync(code, session.CurrentRound, userId);
                    if (isDuplicate)
                    {
                        _logger.LogWarning(ex, "Duplicate submission detected for Game {Code}, Round {Round}, User {UserId}. Ignoring.", 
                            code, session.CurrentRound, userId);
                        return;
                    }
                    else
                    {
                        throw new HubException("Failed to persist answers to database.", ex);
                    }
                }

                // 3. Record in memory only after DB success
                _gameSessionService.TrySubmitAnswers(code, userId, answers);

                // 4. Check if all players have submitted to trigger the validation phase
                if (_gameSessionService.CheckAllAnswersSubmitted(code))
                {
                    // Build RoundAnswersDto from the DB (has DictionaryValid + RequiresPeerReview)
                    var scoringData = await _gameOperationService.GetRoundAnswersDtoAsync(
                        code, session.CurrentRound);
                    await Clients.Group(code).SendAsync("DisplayScoring", scoringData);
                }
            }
        }
    }

    private async Task StartRoundInternal(string code)
    {
        var session = _gameSessionService.TryGetSession(code);
        if (session == null) return;

        // 1. Try to pick a new letter. This internally checks for round limits and alphabet exhaustion.
        var (letter, cancellationToken, gameOverReason) = _gameSessionService.StartNextRound(code);
        
        if (gameOverReason != null)
        {
            var leaderboard = await _scoringService.GetLeaderboardAsync(code, gameOverReason);
            await Clients.Group(code).SendAsync("GameOver", leaderboard);
            return;
        }

        await Clients.Group(code).SendAsync("RoundStarted", new
        {
            roundNumber = session.CurrentRound,
            letter = letter!.ToString(),
            timerDuration = session.TimerDuration,
            serverTime = DateTime.UtcNow.ToString("o")
        });


        // Run the background timer task for Option A
        // Using the token provided by the session service
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(session.TimerDuration * 1000, cancellationToken);
                // If we reach here, the timer expired naturally
                _gameSessionService.LockRound(code);
                await _hubContext.Clients.Group(code).SendAsync("RoundStopped", new { callerNickname = "Timer" });
            }
            catch (TaskCanceledException)
            {
                // Round was locked via "Basta!" call by a player
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background timer task failed for Game {Code}, Round {Round}", code, session.CurrentRound);
            }
        });
    }

    private async Task LockRoundInternal(string code, string nickname)
    {
        _gameSessionService.LockRound(code);
        await Clients.Group(code).SendAsync("RoundStopped", new { callerNickname = nickname });
    }

    /// <summary>
    /// Updates the game session settings (rounds, timer, categories) for the current lobby.
    /// Only the host can perform this action.
    /// </summary>
    public async Task UpdateGameSettings(int totalRounds, int timerDuration, List<int> categoryIds, string language)
    {
        if (Context.Items.TryGetValue("GameCode", out var codeObj) && codeObj is string code &&
            Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int userId)
        {
            var session = _gameSessionService.TryGetSession(code);
            if (session != null)
            {
                if (session.HostUserId == userId)
                {
                    try
                    {
                        _gameSessionService.UpdateSessionSettings(code, totalRounds, timerDuration, categoryIds, language);
                        await _gameOperationService.UpdateGameSettingsAsync(code, totalRounds, timerDuration, language);

                        var snapshot = _gameSessionService.GetLobbySnapshot(code);
                        if (snapshot != null)
                        {
                            await Clients.Group(code).SendAsync("ReceiveLobbyUpdate", snapshot);
                        }
                    }
                    catch (ArgumentException ex)
                    {
                        await Clients.Caller.SendAsync("Error", ex.Message);
                    }
                }
                else
                {
                    await Clients.Caller.SendAsync("Error", "Only the host can update game settings.");
                }
            }
        }
    }

    public async Task SubmitValidation(Dictionary<int, bool> validations)
    {
        if (Context.Items.TryGetValue("GameCode", out var codeObj) && codeObj is string code &&
            Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int userId)
        {
            var session = _gameSessionService.TryGetSession(code);
            if (session != null)
            {
                // 1. Persist the IsValid flags to the database
                await _gameOperationService.UpdateValidationAsync(code, session.CurrentRound, userId, validations);

                // 2. Record in-memory that this user has finished validating
                if (_gameSessionService.SubmitValidation(code, userId))
                {
                    // 3. If everyone is done, calculate final scores and broadcast results
                    var finalScores = await _scoringService.CalculateAndAwardPointsAsync(
                        code, 
                        session.CurrentRound, 
                        session.CurrentLetter ?? ' ');

                    await Clients.Group(code).SendAsync("ReceiveGameScore", finalScores);
                }
            }
        }
        else
        {
            throw new HubException("Not authenticated");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("GameCode", out var codeObj) && codeObj is string code &&
            Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int userId)
        {
            var session = _gameSessionService.TryGetSession(code);
            if (session != null)
            {
                _gameSessionService.MarkPlayerOffline(code, userId);

                if (session.CurrentRound == 0)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(15));
                        var currentSession = _gameSessionService.TryGetSession(code);
                        if (currentSession != null && currentSession.OfflinePlayers.Contains(userId))
                        {
                            _gameSessionService.RemovePlayer(code, userId);
                            var snapshot = _gameSessionService.GetLobbySnapshot(code);
                            if (snapshot != null)
                            {
                                await _hubContext.Clients.Group(code).SendAsync("ReceiveLobbyUpdate", snapshot);
                            }
                        }
                    });
                }

                var snapshot = _gameSessionService.GetLobbySnapshot(code);
                if (snapshot != null)
                {
                    await Clients.Group(code).SendAsync("ReceiveLobbyUpdate", snapshot);
                }
            }
        }
        
        await base.OnDisconnectedAsync(exception);
    }
}
