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
    private readonly Mock<IScoringService> _mockScoringService;
    private readonly Mock<IHubContext<BastaHub>> _mockHubContext;
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
        _mockScoringService = new Mock<IScoringService>();
        _mockHubContext = new Mock<IHubContext<BastaHub>>();
        _mockLogger = new Mock<ILogger<BastaHub>>();
        _mockClients = new Mock<IHubCallerClients>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockSingleClientProxy = new Mock<ISingleClientProxy>();
        _mockContext = new Mock<HubCallerContext>();

        _sut = new BastaHub(
            _mockSessionService.Object, 
            _mockOperationService.Object, 
            _mockScoringService.Object, 
            _mockHubContext.Object,
            _mockLogger.Object)
        {
            Clients = _mockClients.Object,
            Context = _mockContext.Object
        };


        // Default mock behavior for Clients.Group(code)
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        _mockClients.Setup(c => c.Caller).Returns(_mockSingleClientProxy.Object);
        _mockOperationService.Setup(o => o.ValidatePlayerAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockSessionService.Setup(s => s.IsRoundAcceptingAnswers(It.IsAny<string>())).Returns(true);
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
        
        string? msg = null;
        _mockSessionService.Setup(s => s.TryAddPlayer(code, userId, nickname, out msg)).Returns(true);

        var mockGroups = new Mock<IGroupManager>();
        _sut.Groups = mockGroups.Object;

        // Act
        await _sut.JoinGame(code, userId, nickname);

        // Assert
        mockGroups.Verify(g => g.AddToGroupAsync(connectionId, code, default), Times.Once);
        _mockClients.Verify(c => c.Group(code), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveLobbyUpdate", It.Is<object?[]>(o => (LobbySnapshot)o[0]! == snapshot), default), Times.Once);
    }

    [Fact]
    public async Task JoinGame_CallsTryAddPlayer_WhenPlayerJoins()
    {
        // Arrange
        var code = "ABCD";
        var userId = 2;
        var nickname = "Joiner";
        var snapshot = new LobbySnapshot { GameCode = code };

        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
        _mockSessionService.Setup(s => s.GetLobbySnapshot(code)).Returns(snapshot);
        
        string? msg = null;
        _mockSessionService.Setup(s => s.TryAddPlayer(code, userId, nickname, out msg)).Returns(true);

        var mockGroups = new Mock<IGroupManager>();
        _sut.Groups = mockGroups.Object;

        // Act
        await _sut.JoinGame(code, userId, nickname);

        // Assert
        string? errorMessage;
        _mockSessionService.Verify(s => s.TryAddPlayer(code, userId, nickname, out errorMessage), Times.Once);
    }

    [Fact]
    public async Task JoinGame_BroadcastsSnapshot_EvenIfTryAddPlayerReturnsFalse()
    {
        // Arrange
        var code = "ABCD";
        var userId = 1;
        var nickname = "Host";
        var snapshot = new LobbySnapshot { GameCode = code };

        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
        _mockSessionService.Setup(s => s.GetLobbySnapshot(code)).Returns(snapshot);
        
        // Simulate player already in session (returns false)
        string? msg = "Already registered in this game";
        _mockSessionService.Setup(s => s.TryAddPlayer(code, userId, nickname, out msg)).Returns(false);

        var mockGroups = new Mock<IGroupManager>();
        _sut.Groups = mockGroups.Object;

        // Act
        await _sut.JoinGame(code, userId, nickname);

        // Assert
        _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveLobbyUpdate", It.Is<object?[]>(o => (LobbySnapshot)o[0]! == snapshot), default), Times.Once);
    }

    [Fact]
    public async Task JoinGame_ThrowsHubException_WhenTryAddPlayerFails()
    {
        // Arrange
        var code = "ABCD";
        var userId = 3;
        var nickname = "Latecomer";
        var errorMessage = "Game is already full";

        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
        
        string? msg = errorMessage;
        _mockSessionService.Setup(s => s.TryAddPlayer(code, userId, nickname, out msg)).Returns(false);

        var mockGroups = new Mock<IGroupManager>();
        _sut.Groups = mockGroups.Object;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HubException>(() => _sut.JoinGame(code, userId, nickname));
        Assert.Equal(errorMessage, ex.Message);
        
        // Verify no broadcast was sent
        _mockClients.Verify(c => c.Group(code), Times.Never);
        _mockClientProxy.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default), Times.Never);
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
        _mockSessionService.Setup(s => s.StartNextRound(code)).Returns(('A', new CancellationTokenSource().Token, null));

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
        var language = "es";

        var items = new Dictionary<object, object?> { { "GameCode", code }, { "UserId", userId } };
        _mockContext.Setup(c => c.Items).Returns(items);
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);

        // Act
        await _sut.UpdateGameSettings(rounds, timer, categories, language);

        // Assert
        _mockSessionService.Verify(s => s.UpdateSessionSettings(code, rounds, timer, categories, language), Times.Once);
        _mockOperationService.Verify(o => o.UpdateGameSettingsAsync(code, rounds, timer, language, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateGameSettings_DoesNotCallService_WhenNotHost()
    {
        // Arrange
        var code = "ABCD";
        var userId = 2;
        var hostUserId = 1; // Not the caller
        var session = new GameSession { HostUserId = hostUserId };
        var language = "es";

        var items = new Dictionary<object, object?> { { "GameCode", code }, { "UserId", userId } };
        _mockContext.Setup(c => c.Items).Returns(items);
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);

        // Act
        await _sut.UpdateGameSettings(5, 60, new List<int> { 1 }, language);

        // Assert
        _mockSessionService.Verify(s => s.UpdateSessionSettings(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<List<int>>(), It.IsAny<string>()), Times.Never);
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
        _mockSessionService.Setup(s => s.StartNextRound(code)).Returns(((char?)null, CancellationToken.None, "No more letters available."));
        
        var leaderboard = new LeaderboardDto("No more letters available.", new List<LeaderboardPlayerDto>());
        _mockScoringService.Setup(s => s.GetLeaderboardAsync(code, "No more letters available.")).ReturnsAsync(leaderboard);

        // Act
        await _sut.StartGame();

        // Assert
        _mockClientProxy.Verify(p => p.SendCoreAsync("GameOver", It.Is<object?[]>(o => (LeaderboardDto)o[0]! == leaderboard), default), Times.Once);
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
        _mockSessionService.Setup(s => s.StartNextRound(code)).Returns(((char?)null, CancellationToken.None, "All rounds completed."));

        var leaderboard = new LeaderboardDto("All rounds completed.", new List<LeaderboardPlayerDto>());
        _mockScoringService.Setup(s => s.GetLeaderboardAsync(code, "All rounds completed.")).ReturnsAsync(leaderboard);

        // Act
        await _sut.StartGame();

        // Assert
        _mockClientProxy.Verify(p => p.SendCoreAsync("GameOver", It.Is<object?[]>(o => (LeaderboardDto)o[0]! == leaderboard), default), Times.Once);
        _mockSessionService.Verify(s => s.StartNextRound(code), Times.Once);
    }

    [Fact]
    public async Task SubmitValidation_LastPlayer_TriggersScoring()
    {
        // Arrange
        var code = "TEST";
        var userId = 1;
        var validations = new Dictionary<int, bool> { { 1, true } };
        var session = new GameSession { Code = code, CurrentRound = 1, CurrentLetter = 'A' };

        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?> { 
            { "GameCode", code }, 
            { "UserId", userId } 
        });

        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);
        _mockSessionService.Setup(s => s.SubmitValidation(code, userId)).Returns(true); // Last player
        
        var scores = new List<PlayerScoreDto> { new PlayerScoreDto(1, "Alice", 10, 10, new List<AnswerScoreDto>()) };
        _mockScoringService.Setup(s => s.CalculateAndAwardPointsAsync(code, 1, 'A')).ReturnsAsync(scores);

        // Act
        await _sut.SubmitValidation(validations);

        // Assert
        _mockOperationService.Verify(o => o.UpdateValidationAsync(code, 1, userId, validations, It.IsAny<CancellationToken>()), Times.Once);
        _mockScoringService.Verify(s => s.CalculateAndAwardPointsAsync(code, 1, 'A'), Times.Once);
        _mockClientProxy.Verify(c => c.SendCoreAsync("ReceiveGameScore", It.Is<object?[]>(o => (List<PlayerScoreDto>)o[0]! == scores), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinGame_ThrowsHubException_AndDoesNotJoinGroup_WhenPlayerIsNotRegistered()
    {
        // Arrange
        var code = "ABCD";
        var userId = 99;
        var nickname = "Intruder";
        var connectionId = "conn-999";

        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);
        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
        _mockOperationService
            .Setup(o => o.ValidatePlayerAsync(code, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var mockGroups = new Mock<IGroupManager>();
        _sut.Groups = mockGroups.Object;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HubException>(() => _sut.JoinGame(code, userId, nickname));
        Assert.Equal("Player is not registered for this game session.", ex.Message);

        // Verify that Groups.AddToGroupAsync was never called
        mockGroups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        // Verify that GetLobbySnapshot was never called
        _mockSessionService.Verify(s => s.GetLobbySnapshot(code), Times.Never);
    }

    [Fact]
    public async Task OnDisconnectedAsync_CurrentRoundGreaterThanZero_MarksPlayerOfflineAndBroadcasts()
    {
        // Arrange
        var code = "ABCD";
        var userId = 42;
        var session = new GameSession { Code = code, CurrentRound = 1 };
        var snapshot = new LobbySnapshot { GameCode = code };

        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>
        {
            { "GameCode", code },
            { "UserId", userId }
        });
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);
        _mockSessionService.Setup(s => s.GetLobbySnapshot(code)).Returns(snapshot);

        // Act
        await _sut.OnDisconnectedAsync(null);

        // Assert
        _mockSessionService.Verify(s => s.MarkPlayerOffline(code, userId), Times.Once);
        _mockSessionService.Verify(s => s.RemovePlayer(code, userId), Times.Never);
        _mockClients.Verify(c => c.Group(code), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveLobbyUpdate", It.Is<object?[]>(o => (LobbySnapshot)o[0]! == snapshot), default), Times.Once);
    }

    [Fact]
    public async Task OnDisconnectedAsync_CurrentRoundZero_MarksPlayerOfflineAndDoesNotRemovePlayerImmediately()
    {
        // Arrange
        var code = "ABCD";
        var userId = 42;
        var session = new GameSession { Code = code, CurrentRound = 0 };
        var snapshot = new LobbySnapshot { GameCode = code };

        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>
        {
            { "GameCode", code },
            { "UserId", userId }
        });
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);
        _mockSessionService.Setup(s => s.GetLobbySnapshot(code)).Returns(snapshot);

        // Act
        await _sut.OnDisconnectedAsync(null);

        // Assert
        _mockSessionService.Verify(s => s.MarkPlayerOffline(code, userId), Times.Once);
        // Verify it was not removed synchronously
        _mockSessionService.Verify(s => s.RemovePlayer(code, userId), Times.Never);
        _mockClients.Verify(c => c.Group(code), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveLobbyUpdate", It.Is<object?[]>(o => (LobbySnapshot)o[0]! == snapshot), default), Times.Once);
    }

    [Fact]
    public async Task SubmitAnswers_WhenDbThrowsDbUpdateExceptionAndHasSubmitted_LogsWarningAndReturnsCleanly()
    {
        // Arrange
        var code = "ABCD";
        var userId = 1;
        var currentRound = 2;
        var session = new GameSession { Code = code, CurrentRound = currentRound };
        var answers = new Dictionary<int, string> { { 1, "Answer" } };

        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>
        {
            { "GameCode", code },
            { "UserId", userId }
        });
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);
        _mockOperationService
            .Setup(o => o.SubmitAnswersAsync(code, currentRound, userId, answers, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException("Constraint violation"));
        _mockOperationService
            .Setup(o => o.HasSubmittedAsync(code, currentRound, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Yes, already submitted!

        // Act
        await _sut.SubmitAnswers(answers);

        // Assert: should not throw, should verify HasSubmittedAsync was checked
        _mockOperationService.Verify(o => o.HasSubmittedAsync(code, currentRound, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAnswers_WhenDbThrowsDbUpdateExceptionAndNotSubmitted_ThrowsHubException()
    {
        // Arrange
        var code = "ABCD";
        var userId = 1;
        var currentRound = 2;
        var session = new GameSession { Code = code, CurrentRound = currentRound };
        var answers = new Dictionary<int, string> { { 1, "Answer" } };

        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>
        {
            { "GameCode", code },
            { "UserId", userId }
        });
        _mockSessionService.Setup(s => s.TryGetSession(code)).Returns(session);
        _mockOperationService
            .Setup(o => o.SubmitAnswersAsync(code, currentRound, userId, answers, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException("Constraint violation"));
        _mockOperationService
            .Setup(o => o.HasSubmittedAsync(code, currentRound, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // No, not submitted! Genuine DB error.

        // Act & Assert
        await Assert.ThrowsAsync<HubException>(() => _sut.SubmitAnswers(answers));
        _mockOperationService.Verify(o => o.HasSubmittedAsync(code, currentRound, userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}


