using Basta.Server.DTOs;
using Basta.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Basta.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly IGameOperationService _gameOperationService;
    private readonly IGameSessionService _gameSessionService;

    public GamesController(IGameOperationService gameOperationService, IGameSessionService gameSessionService)
    {
        _gameOperationService = gameOperationService;
        _gameSessionService = gameSessionService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateGameResponse>> CreateGame(
        [FromBody] CreateGameRequest request,
        CancellationToken cancellationToken)
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

        var result = await _gameOperationService.CreateGameAsync(
            nickname,
            request.PreferredLanguage,
            request.TotalRounds,
            request.TimerDuration,
            request.CategoryIds,
            cancellationToken);

        return Ok(new CreateGameResponse
        {
            GameCode = result.GameCode,
            HostUserId = result.HostUserId
        });
    }

    [HttpGet("{code}")]
    public ActionResult<LobbySnapshot> GetGame(string code)
    {
         var snapshot = _gameSessionService.GetLobbySnapshot(code.ToUpperInvariant());
         if (snapshot is null)
         {
             return NotFound(new ProblemDetails 
             { 
                 Title = "Game not found.", 
                 Detail = $"No active game found with code '{code}'." 
             });
         }

         return Ok(snapshot);
    }

    [HttpPost("{code}/join")]
    public async Task<ActionResult<JoinGameResponse>> JoinGame(
        string code,
        [FromBody] JoinGameRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var nickname = request.Nickname.Trim();
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return BadRequest("Nickname cannot be empty or solely whitespace.");
        }

        try
        {
            var result = await _gameOperationService.JoinGameAsync(
                code.ToUpperInvariant(),
                nickname,
                request.PreferredLanguage,
                cancellationToken);

            return Ok(new JoinGameResponse
            {
                GameCode = result.GameCode,
                UserId = result.UserId
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Join failed.",
                Detail = ex.Message
            });
        }
    }
}
