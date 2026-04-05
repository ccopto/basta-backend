using Basta.Server.Data;
using Basta.Server.DTOs;
using Basta.Server.Entities;
using Basta.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Basta.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly BastaDbContext _context;
    private readonly IGameSessionService _gameSessionService;

    public GamesController(BastaDbContext context, IGameSessionService gameSessionService)
    {
        _context = context;
        _gameSessionService = gameSessionService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateGameResponse>> CreateGame([FromBody] CreateGameRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var nickname = request.HostNickname.Trim();
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return BadRequest("Nickname cannot be empty or solely whitespace.");
        }

        // 1. Create the User (Host)
        var host = new User
        {
            Nickname = nickname,
            PreferredLanguage = request.PreferredLanguage
        };

        _context.Users.Add(host);
        await _context.SaveChangesAsync();

        // 2. Generate the Session Code and initialize in-memory state
        var code = _gameSessionService.CreateSession(host.UserId, request.TotalRounds, request.TimerDuration);

        // 3. Create the Game entity for DB persistence
        var game = new Game
        {
            GameId = code,
            HostUserId = host.UserId,
            TotalRounds = request.TotalRounds,
            TimerDuration = request.TimerDuration
        };

        _context.Games.Add(game);
        
        // 4. Add the Host as a Player to the DB
        var gamePlayer = new GamePlayer
        {
            GameId = code,
            UserId = host.UserId
        };
        _context.GamePlayers.Add(gamePlayer);

        await _context.SaveChangesAsync();

        return Ok(new CreateGameResponse
        {
            GameCode = code,
            HostUserId = host.UserId
        });
    }
}
