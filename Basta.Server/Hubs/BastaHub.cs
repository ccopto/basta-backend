using Microsoft.AspNetCore.SignalR;

using Basta.Server.Services;

namespace Basta.Server.Hubs;

public class BastaHub : Hub
{
    private readonly IGameSessionService _gameSessionService;
    private readonly IGameOperationService _gameOperationService;

    public BastaHub(IGameSessionService gameSessionService, IGameOperationService gameOperationService)
    {
        _gameSessionService = gameSessionService;
        _gameOperationService = gameOperationService;
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
                // Note: We allows submission even if locked if it's the FINAL submission
                // but usually the client sends this immediately upon RoundStopped signal.
                if (_gameSessionService.TrySubmitAnswers(code, userId, answers))
                {
                    // 2. Persist to DB for posterity and scoring
                    await _gameOperationService.SubmitAnswersAsync(code, session.CurrentRound, userId, answers);
                }
            }
        }
    }

    private async Task StartRoundInternal(string code)
    {
        var session = _gameSessionService.TryGetSession(code);
        if (session == null) return;

        var letter = _gameSessionService.StartNextRound(code);
        if (letter == null)
        {
            await Clients.Group(code).SendAsync("Error", "No more letters available.");
            return;
        }

        // Initialize and store the timer CTS
        var cts = new CancellationTokenSource();
        session.RoundTimerCts = cts;

        await Clients.Group(code).SendAsync("RoundStarted", new
        {
            roundNumber = session.CurrentRound,
            letter = letter.ToString(),
            timerDuration = session.TimerDuration,
            serverTime = DateTime.UtcNow.ToString("o")
        });

        // Run the background timer task for Option A
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(session.TimerDuration * 1000, cts.Token);
                // If we reach here, the timer expired naturally
                await LockRoundInternal(code, "Timer");
            }
            catch (TaskCanceledException)
            {
                // Round was locked via "Basta!" call by a player
            }
            catch (Exception)
            {
                // Handle or log other errors
            }
        });
    }

    private async Task LockRoundInternal(string code, string nickname)
    {
        _gameSessionService.LockRound(code);
        await Clients.Group(code).SendAsync("RoundStopped", new { callerNickname = nickname });
    }

    public async Task SetCategories(List<int> categoryIds)
    {
        if (Context.Items.TryGetValue("GameCode", out var codeObj) && codeObj is string code &&
            Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is int userId)
        {
            var session = _gameSessionService.TryGetSession(code);
            if (session != null && session.HostUserId == userId)
            {
                session.SelectedCategoryIds = categoryIds;
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
