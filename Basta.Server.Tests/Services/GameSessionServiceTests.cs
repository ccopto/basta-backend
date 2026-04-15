using Basta.Server.Services;
using FluentAssertions;
using Xunit;

namespace Basta.Server.Tests.Services;

public class GameSessionServiceTests
{
    private readonly GameSessionService _sut;

    public GameSessionServiceTests()
    {
        _sut = new GameSessionService();
    }

    [Fact]
    public void CreateSession_ReturnsValidFourLetterCode()
    {
        // Act
        var code = _sut.CreateSession(1, 5, 60);

        // Assert
        code.Should().NotBeNullOrWhiteSpace();
        code.Length.Should().Be(4);
        // Character set excludes visually ambiguous letters: I, O, Q, V, Z
        code.Should().MatchRegex("^[ABCDEFGHJKLMNPRSTUWXY]{4}$");
    }

    [Fact]
    public void CreateSession_PopulatesSessionObject()
    {
        // Arrange
        var hostId = 42;
        var rounds = 10;
        var timer = 30;

        // Act
        var code = _sut.CreateSession(hostId, rounds, timer);
        var session = _sut.TryGetSession(code);

        // Assert
        session.Should().NotBeNull();
        session!.Code.Should().Be(code);
        session.HostUserId.Should().Be(hostId);
        session.TotalRounds.Should().Be(rounds);
        session.TimerDuration.Should().Be(timer);
    }

    [Fact]
    public void TryGetSession_WithInvalidCode_ReturnsNull()
    {
        // Act
        var session = _sut.TryGetSession("XYZ9");

        // Assert
        session.Should().BeNull();
    }

    [Fact]
    public void TryAddPlayer_ValidJoin_AddsPlayerToSession()
    {
        // Arrange
        var code = _sut.CreateSession(1, 5, 60);

        // Act
        var result = _sut.TryAddPlayer(code, 2, "TestJoin", out var errorMessage);

        // Assert
        result.Should().BeTrue();
        errorMessage.Should().BeEmpty();
        var session = _sut.TryGetSession(code);
        session!.Players.Should().ContainKey(2).WhoseValue.Should().Be("TestJoin");
    }

    [Fact]
    public void TryAddPlayer_RoomFull_ReturnsFalse()
    {
        // Arrange
        var code = _sut.CreateSession(1, 5, 60);
        _sut.TryAddPlayer(code, 1, "Host", out _);
        _sut.TryAddPlayer(code, 2, "P2", out _);
        _sut.TryAddPlayer(code, 3, "P3", out _);
        _sut.TryAddPlayer(code, 4, "P4", out _);
        _sut.TryAddPlayer(code, 5, "P5", out _);

        // Act
        var result = _sut.TryAddPlayer(code, 6, "Overflow", out var errorMessage);

        // Assert
        result.Should().BeFalse();
        errorMessage.Should().Be("Game session is full.");
        
        var session = _sut.TryGetSession(code);
        session!.Players.Should().NotContainKey(6);
    }
}
