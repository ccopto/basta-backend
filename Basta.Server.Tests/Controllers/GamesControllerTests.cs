using Basta.Server.Controllers;
using Basta.Server.Data;
using Basta.Server.DTOs;
using Basta.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Basta.Server.Tests.Controllers;

public class GamesControllerTests : IDisposable
{
    private readonly BastaDbContext _context;
    private readonly Mock<IGameSessionService> _mockSessionService;
    private readonly GamesController _sut;

    public GamesControllerTests()
    {
        var options = new DbContextOptionsBuilder<BastaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new BastaDbContext(options);
        _mockSessionService = new Mock<IGameSessionService>();

        _sut = new GamesController(_context, _mockSessionService.Object);
    }

    [Fact]
    public async Task CreateGame_ValidRequest_ReturnsOkWithCode()
    {
        // Arrange
        var request = new CreateGameRequest
        {
            HostNickname = "TestHost",
            PreferredLanguage = "es",
            TotalRounds = 5,
            TimerDuration = 60
        };

        var expectedCode = "ABCD";
        _mockSessionService
            .Setup(s => s.CreateSession(It.IsAny<int>(), request.TotalRounds, request.TimerDuration))
            .Returns(expectedCode);

        // Act
        var result = await _sut.CreateGame(request);

        // Assert
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        
        var response = okResult!.Value as CreateGameResponse;
        response.Should().NotBeNull();
        response!.GameCode.Should().Be(expectedCode);
        response.HostUserId.Should().BeGreaterThan(0);

        // Verify DB State
        var user = await _context.Users.FindAsync(response.HostUserId);
        user.Should().NotBeNull();
        user!.Nickname.Should().Be("TestHost");

        var game = await _context.Games.FindAsync(expectedCode);
        game.Should().NotBeNull();
        game!.HostUserId.Should().Be(response.HostUserId);

        var player = await _context.GamePlayers.FirstOrDefaultAsync(p => p.GameId == expectedCode);
        player.Should().NotBeNull();
        player!.UserId.Should().Be(response.HostUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateGame_EmptyNickname_ReturnsBadRequest(string invalidNickname)
    {
        // Arrange
        var request = new CreateGameRequest
        {
            HostNickname = invalidNickname
        };

        // If it's null, we simulate ASP.NET Core validation bypassing it slightly 
        // to test the manual check inside the controller. Actually, [Required] catches null.
        if (invalidNickname == null)
            _sut.ModelState.AddModelError("HostNickname", "Required");

        // Act
        var result = await _sut.CreateGame(request);

        // Assert
        var badRequestResult = result.Result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
