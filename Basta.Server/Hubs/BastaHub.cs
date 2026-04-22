using Microsoft.AspNetCore.SignalR;

using Basta.Server.Services;

namespace Basta.Server.Hubs;

public class BastaHub : Hub
{
    private readonly IGameSessionService _gameSessionService;
    private readonly IGameOperationService _gameOperationService;
    private readonly ILogger<BastaHub> _logger;

    public BastaHub(
        IGameSessionService gameSessionService, 
        IGameOperationService gameOperationService,
        ILogger<BastaHub> logger)
    {
        _gameSessionService = gameSessionService;
        _gameOperationService = gameOperationService;
        _logger = logger;
    }

    public async Task JoinGame(string code, int userId, string nickname)
    {
        // 1. Add connection to the SignalR group for the specific game code.
        code = code.ToUpperInvariant();
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        // 2. Track userId mapped to this connection for disconnection handling later
        Context.Items["UserId"] = userId;
        Context.Items["GameCode"] = code;

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
                // 1. First record in-memory. This checks if the round is already locked.
                if (_gameSessionService.TrySubmitAnswers(code, userId, answers))
                {
                    try
                    {
                        // 2. Persist to DB for posterity and scoring
                        await _gameOperationService.SubmitAnswersAsync(code, session.CurrentRound, userId, answers);
                    }
                    catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                    {
                        // Handle race condition: If another thread already persisted for this (Game, Round, User, Category),
                        // we treat it as idempotent and just log a warning.
                        _logger.LogWarning(ex, "Duplicate submission detected for Game {Code}, Round {Round}, User {UserId}. Ignoring.", 
                            code, session.CurrentRound, userId);
                    }
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
            await Clients.Group(code).SendAsync("GameOver", gameOverReason);
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
                await LockRoundInternal(code, "Timer");
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
    public async Task UpdateGameSettings(int totalRounds, int timerDuration, List<int> categoryIds)
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
                        _gameSessionService.UpdateSessionSettings(code, totalRounds, timerDuration, categoryIds);
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



    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("GameCode", out var codeObj) && codeObj is string code &&
            Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int userId)
        {
            // Remove the user from the in-memory session tracking
            _gameSessionService.RemovePlayer(code, userId);

            // Fetch the updated snapshot to broadcast to the remaining players
            var snapshot = _gameSessionService.GetLobbySnapshot(code);
            
            if (snapshot != null)
            {
                await Clients.Group(code).SendAsync("ReceiveLobbyUpdate", snapshot);
            }
        }
        
        await base.OnDisconnectedAsync(exception);
    }
}
