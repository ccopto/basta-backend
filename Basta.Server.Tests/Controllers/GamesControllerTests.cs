using Basta.Server.Controllers;
using Basta.Server.DTOs;
using Basta.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Basta.Server.Tests.Controllers;

public class GamesControllerTests
{
    private readonly Mock<IGameOperationService> _mockGameOperationService;
    private readonly Mock<IGameSessionService> _mockGameSessionService;
    private readonly GamesController _sut;

    public GamesControllerTests()
    {
        _mockGameOperationService = new Mock<IGameOperationService>();
        _mockGameSessionService = new Mock<IGameSessionService>();
        _sut = new GamesController(_mockGameOperationService.Object, _mockGameSessionService.Object);
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

        var expectedResult = new CreateGameResult("ABCD", 1);
        _mockGameOperationService
            .Setup(s => s.CreateGameAsync("TestHost", "es", 5, 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _sut.CreateGame(request, CancellationToken.None);

        // Assert
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var response = okResult!.Value as CreateGameResponse;
        response.Should().NotBeNull();
        response!.GameCode.Should().Be("ABCD");
        response.HostUserId.Should().Be(1);

        _mockGameOperationService.Verify(
            s => s.CreateGameAsync("TestHost", "es", 5, 60, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateGame_EmptyNickname_ReturnsBadRequest(string? invalidNickname)
    {
        // Arrange
        var request = new CreateGameRequest
        {
            HostNickname = invalidNickname!
        };

        // Simulate ASP.NET Core model binding for null (which [Required] would reject)
        if (invalidNickname == null)
            _sut.ModelState.AddModelError("HostNickname", "Required");

        // Act
        var result = await _sut.CreateGame(request, CancellationToken.None);

        // Assert
        var badRequestResult = result.Result as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();

        // Service should never be called on invalid input
        _mockGameOperationService.Verify(
            s => s.CreateGameAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
