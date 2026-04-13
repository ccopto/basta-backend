using Basta.Server.DTOs;
using Basta.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Basta.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly IGameOperationService _gameOperationService;

    public GamesController(IGameOperationService gameOperationService)
    {
        _gameOperationService = gameOperationService;
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

        var result = await _gameOperationService.CreateGameAsync(
            nickname,
            request.PreferredLanguage,
            request.TotalRounds,
            request.TimerDuration);

        return Ok(new CreateGameResponse
        {
            GameCode = result.GameCode,
            HostUserId = result.HostUserId
        });
    }
}
