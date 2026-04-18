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
        var code = _sut.CreateSession(1, 5, 60, new List<int> { 1 });

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
        var code = _sut.CreateSession(hostId, rounds, timer, new List<int> { 1 });
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
        var code = _sut.CreateSession(1, 5, 60, new List<int> { 1 });

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
        var code = _sut.CreateSession(1, 5, 60, new List<int> { 1 });
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

    [Fact]
    public void TryAddPlayer_DuplicateNickname_ReturnsFalse()
    {
        // Arrange
        var code = _sut.CreateSession(1, 5, 60, new List<int> { 1 });
        _sut.TryAddPlayer(code, 2, "DuplicateName", out _);

        // Act
        var result = _sut.TryAddPlayer(code, 3, "duplicatename", out var errorMessage);

        // Assert
        result.Should().BeFalse();
        errorMessage.Should().Be("Nickname is already taken in this session.");
        
        var session = _sut.TryGetSession(code);
        session!.Players.Should().NotContainKey(3);
    }

    [Fact]
    public void TryAddPlayer_SameUserId_UpdatesSafely()
    {
        // Arrange
        var code = _sut.CreateSession(1, 5, 60, new List<int> { 1 });
        _sut.TryAddPlayer(code, 2, "FirstName", out _);

        // Act
        var result = _sut.TryAddPlayer(code, 2, "SecondName", out var errorMessage);

        // Assert
        result.Should().BeTrue();
        errorMessage.Should().BeEmpty();
        
        var session = _sut.TryGetSession(code);
        session!.Players.Should().ContainKey(2).WhoseValue.Should().Be("SecondName");
        session.Players.Should().HaveCount(1);
    }

    [Fact]
    public void TryAddPlayer_InvalidGameCode_ReturnsFalse()
    {
        // Act
        var result = _sut.TryAddPlayer("INVALID", 1, "Name", out var errorMessage);

        // Assert
        result.Should().BeFalse();
        errorMessage.Should().Be("Game session not found.");
    }

    [Fact]
    public void StartNextRound_PicksLetterAndUpdatesState()
    {
        // Arrange
        var code = _sut.CreateSession(1, 5, 60, new List<int> { 1 });

        // Act
        var (letter, _) = _sut.StartNextRound(code);

        // Assert
        letter.Should().NotBeNull();
        var session = _sut.TryGetSession(code);
        session!.CurrentRound.Should().Be(1);
        session.CurrentLetter.Should().Be(letter);
        session.UsedLetters.Should().Contain(letter.Value);
        session.RoundActive.Should().BeTrue();
        session.RoundLocked.Should().BeFalse();
    }

    [Fact]
    public void StartNextRound_ReturnsNull_WhenNoLettersLeft()
    {
        // Arrange
        var code = _sut.CreateSession(1, 100, 60, new List<int> { 1 });
        const string alphabet = "ABCDEFGHJKLMNPRSTUWXY"; // 21 letters
        for (int i = 0; i < alphabet.Length; i++)
        {
            _sut.StartNextRound(code);
        }

        // Act
        var (letter, _) = _sut.StartNextRound(code);

        // Assert
        letter.Should().BeNull();
    }

    [Fact]
    public void LockRound_UpdatesStateAndClearsTimer()
    {
        // Arrange
        var code = _sut.CreateSession(1, 5, 60, new List<int> { 1 });
        _sut.StartNextRound(code);

        // Act
        _sut.LockRound(code);

        // Assert
        var session = _sut.TryGetSession(code);
        session!.RoundLocked.Should().BeTrue();
        session.RoundActive.Should().BeFalse();
    }

    [Fact]
    public void TrySubmitAnswers_RecordsAnswers_WhenNotLocked()
    {
        // Arrange
        var code = _sut.CreateSession(1, 5, 60, new List<int> { 1 });
        _sut.StartNextRound(code);
        var answers = new Dictionary<int, string> { { 1, "Apple" } };

        // Act
        var result = _sut.TrySubmitAnswers(code, 2, answers);

        // Assert
        result.Should().BeTrue();
        var session = _sut.TryGetSession(code);
        session!.CurrentRoundAnswers.Should().ContainKey(2).WhoseValue.Should().BeEquivalentTo(answers);
    }

    [Fact]
    public void TrySubmitAnswers_Fails_WhenRoundLocked()
    {
        // Arrange
        var code = _sut.CreateSession(1, 5, 60, new List<int> { 1 });
        _sut.StartNextRound(code);
        _sut.LockRound(code);
        var answers = new Dictionary<int, string> { { 1, "Apple" } };

        // Act
        var result = _sut.TrySubmitAnswers(code, 2, answers);

        // Assert
        result.Should().BeFalse();
        var session = _sut.TryGetSession(code);
        session!.CurrentRoundAnswers.Should().NotContainKey(2);
    }
}
