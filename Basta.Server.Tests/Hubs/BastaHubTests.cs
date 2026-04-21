using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Basta.Server.DTOs;
using Basta.Server.Hubs;
using Basta.Server.Models;
using Basta.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Basta.Server.Tests.Hubs;

public class BastaHubTests
{
    private readonly Mock<IGameSessionService> _mockSessionService;
    private readonly Mock<IGameOperationService> _mockOperationService;
    private readonly Mock<ILogger<BastaHub>> _mockLogger;
    private readonly Mock<IHubCallerClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<ISingleClientProxy> _mockSingleClientProxy;
    private readonly Mock<HubCallerContext> _mockContext;
    private readonly BastaHub _sut;

    public BastaHubTests()
    {
        _mockSessionService = new Mock<IGameSessionService>();
        _mockOperationService = new Mock<IGameOperationService>();
        _mockLogger = new Mock<ILogger<BastaHub>>();
        _mockClients = new Mock<IHubCallerClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockSingleClientProxy = new Mock<ISingleClientProxy>();
        _mockContext = new Mock<HubCallerContext>();

        _sut = new BastaHub(_mockSessionService.Object, _mockOperationService.Object, _mockLogger.Object)
        {
            Clients = _mockClients.Object,
            Context = _mockContext.Object
        };

        // Default mock behavior for Clients.Group(code)
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        _mockClients.Setup(c => c.Caller).Returns(_mockSingleClientProxy.Object);
    }

    [Fact]
    public async Task JoinGame_AddsToGroupAndBroadcastsUpdate()
    {
        // Arrange
        var code = "ABCD";
        var userId = 1;
        var nickname = "Tester";
        var connectionId = "conn-123";
        var snapshot = new LobbySnapshot { GameCode = code };

        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);
        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
        _mockSessionService.Setup(s => s.GetLobbySnapshot(code)).Returns(snapshot);

        var mockGroups = new Mock<IGroupManager>();
        _sut.Groups = mockGroups.Object;

        // Act
        await _sut.JoinGame(code, userId, nickname);

        // Assert
        mockGroups.Verify(g => g.AddToGroupAsync(connectionId, code, default), Times.Once);
        _mockClients.Verify(c => c.Group(code), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveLobbyUpdate", It.Is<object?[]>(o => o[0] == snapshot), default), Times.Once);
    }

    [Fact]
    public async Task StartGame_Succeeds_WhenHostAndEnoughPlayers()
    {
        // Arrange
        var code = "ABCD";
        var userId = 1;
        var session = new GameSession 
        { 
            HostUserId = userId, 
            Players = new Dictionary<int, string> { { 1, "H" }, { 2, "P2" } },
            SelectedCategoryIds = new List<int> { 1 },
            TimerDuration = 60,
            TotalRounds = 5
        };


        var items = new Dictionary<object, object?> { { "GameCode", code }, { "UserId", userId } };
        _mockContext.Setup(c => c.Items).Returns(items);
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);
        _mockSessionService.Setup(s => s.StartNextRound(code)).Returns(('A', new CancellationTokenSource().Token));

        // Act
        await _sut.StartGame();

        // Assert
        _mockClientProxy.Verify(p => p.SendCoreAsync("GameStarted", It.IsAny<object?[]>(), default), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("RoundStarted", It.IsAny<object?[]>(), default), Times.Once);
    }

    [Fact]
    public async Task CallBasta_LocksRoundAndBroadcasts()
    {
        // Arrange
        var code = "ABCD";
        var userId = 1;
        var session = new GameSession 
        { 
            RoundActive = true, 
            RoundLocked = false,
            Players = new Dictionary<int, string> { { 1, "Nick" } }
        };

        var items = new Dictionary<object, object?> { { "GameCode", code }, { "UserId", userId } };
        _mockContext.Setup(c => c.Items).Returns(items);
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);

        // Act
        await _sut.CallBasta();

        // Assert
        _mockSessionService.Verify(s => s.LockRound(code), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("RoundStopped", It.Is<object?[]>(o => o.Length > 0 && o[0] != null && o[0]!.ToString()!.Contains("Nick")), default), Times.Once);
    }

    [Fact]
    public async Task UpdateGameSettings_CallsService_WhenHost()

    {
        // Arrange
        var code = "ABCD";
        var userId = 1;
        var session = new GameSession { HostUserId = userId };
        var rounds = 10;
        var timer = 45;
        var categories = new List<int> { 1, 2 };

        var items = new Dictionary<object, object?> { { "GameCode", code }, { "UserId", userId } };
        _mockContext.Setup(c => c.Items).Returns(items);
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);

        // Act
        await _sut.UpdateGameSettings(rounds, timer, categories);

        // Assert
        _mockSessionService.Verify(s => s.UpdateSessionSettings(code, rounds, timer, categories), Times.Once);
    }

    [Fact]
    public async Task UpdateGameSettings_DoesNotCallService_WhenNotHost()
    {
        // Arrange
        var code = "ABCD";
        var userId = 2;
        var hostUserId = 1; // Not the caller
        var session = new GameSession { HostUserId = hostUserId };

        var items = new Dictionary<object, object?> { { "GameCode", code }, { "UserId", userId } };
        _mockContext.Setup(c => c.Items).Returns(items);
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);

        // Act
        await _sut.UpdateGameSettings(5, 60, new List<int> { 1 });

        // Assert
        _mockSessionService.Verify(s => s.UpdateSessionSettings(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<List<int>>()), Times.Never);
        _mockSingleClientProxy.Verify(p => p.SendCoreAsync("Error", It.Is<object?[]>(o => o.Length > 0 && o[0]!.ToString()!.Contains("Only the host")), default), Times.Once);
    }


    [Fact]
    public async Task SubmitAnswers_CallsServiceAndOperationService()
    {

        // Arrange
        var code = "ABCD";
        var userId = 1;
        var currentRound = 2;
        var session = new GameSession { Code = code, CurrentRound = currentRound };
        var answers = new Dictionary<int, string> { { 1, "Answer" } };

        var items = new Dictionary<object, object?> { { "GameCode", code }, { "UserId", userId } };
        _mockContext.Setup(c => c.Items).Returns(items);
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);
        _mockSessionService.Setup(s => s.TrySubmitAnswers(code, userId, answers)).Returns(true);

        // Act
        await _sut.SubmitAnswers(answers);

        // Assert
        _mockSessionService.Verify(s => s.TrySubmitAnswers(code, userId, answers), Times.Once);
        _mockOperationService.Verify(s => s.SubmitAnswersAsync(code, currentRound, userId, answers), Times.Once);
    }

    [Fact]
    public async Task StartGame_BroadcastsGameOver_WhenNoLetters()
    {
        // Arrange
        var code = "ABCD";
        var userId = 1;
        var session = new GameSession 
        { 
            HostUserId = userId, 
            Players = new Dictionary<int, string> { { 1, "H" }, { 2, "P2" } },
            SelectedCategoryIds = new List<int> { 1 },
            TotalRounds = 5,
            CurrentRound = 0
        };

        var items = new Dictionary<object, object?> { { "GameCode", code }, { "UserId", userId } };
        _mockContext.Setup(c => c.Items).Returns(items);
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);
        _mockSessionService.Setup(s => s.StartNextRound(code)).Returns(((char?)null, CancellationToken.None));

        // Act
        await _sut.StartGame();

        // Assert
        _mockClientProxy.Verify(p => p.SendCoreAsync("GameOver", It.Is<object?[]>(o => o[0]!.ToString()!.Contains("No more letters")), default), Times.Once);
    }

    [Fact]
    public async Task StartGame_BroadcastsGameOver_WhenRoundsExceeded()
    {
        // Arrange
        var code = "ABCD";
        var userId = 1;
        var session = new GameSession 
        { 
            HostUserId = userId, 
            Players = new Dictionary<int, string> { { 1, "H" }, { 2, "P2" } },
            SelectedCategoryIds = new List<int> { 1 },
            TotalRounds = 5,
            CurrentRound = 5 // Already at max
        };

        var items = new Dictionary<object, object?> { { "GameCode", code }, { "UserId", userId } };
        _mockContext.Setup(c => c.Items).Returns(items);
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);

        // Act
        await _sut.StartGame();

        // Assert
        _mockClientProxy.Verify(p => p.SendCoreAsync("GameOver", It.Is<object?[]>(o => o[0]!.ToString()!.Contains("All rounds completed")), default), Times.Once);
        _mockSessionService.Verify(s => s.StartNextRound(It.IsAny<string>()), Times.Never);
    }
}

