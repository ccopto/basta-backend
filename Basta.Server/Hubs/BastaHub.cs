using Microsoft.AspNetCore.SignalR;

using Basta.Server.Services;

namespace Basta.Server.Hubs;

public class BastaHub : Hub
{
    private readonly IGameSessionService _gameSessionService;

    public BastaHub(IGameSessionService gameSessionService)
    {
        _gameSessionService = gameSessionService;
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
                        // Broadcast game start to all players in the room
                        await Clients.Group(code).SendAsync("GameStarted");
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
