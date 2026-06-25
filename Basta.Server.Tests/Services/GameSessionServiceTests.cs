using Basta.Server.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;



namespace Basta.Server.Tests.Services;

public class GameSessionServiceTests
{
    private readonly GameSessionService _sut;
    private readonly FakeTimeProvider _timeProvider;
    private readonly Mock<ILogger<GameSessionService>> _loggerMock;

    public GameSessionServiceTests()
    {
        _timeProvider = new FakeTimeProvider();
        _loggerMock = new Mock<ILogger<GameSessionService>>();
        _sut = new GameSessionService(_loggerMock.Object, _timeProvider);
    }

    [Fact]
    public void CreateSession_ReturnsValidFourLetterCode()
    {
        // Act
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });

        // Assert
        code.Should().NotBeNullOrWhiteSpace();
        code.Length.Should().Be(4);
        // Character set excludes visually ambiguous letters: I, O, Q, V, Z
        code.Should().MatchRegex("^[ABCDEFGHJKLMNPRSTUWXY]{4}$");
    }

    [Fact]
    public void CreateSession_PopulatesLanguageAndSnapshotReturnsIt()
    {
        // Arrange
        var hostId = 1;
        var language = "es";

        // Act
        var code = _sut.CreateSession(hostId, "Host", 5, 60, new List<int> { 1 }, language);
        var snapshot = _sut.GetLobbySnapshot(code);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.Language.Should().Be(language);
    }

    [Fact]
    public void CreateSession_PopulatesSessionObject()
    {
        // Arrange
        var hostId = 42;
        var rounds = 10;
        var timer = 30;

        // Act
        var code = _sut.CreateSession(hostId, "Host", rounds, timer, new List<int> { 1 });
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
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });

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
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
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
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
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
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        _sut.TryAddPlayer(code, 2, "FirstName", out _);

        // Act
        var result = _sut.TryAddPlayer(code, 2, "SecondName", out var errorMessage);

        // Assert
        result.Should().BeTrue();
        errorMessage.Should().BeEmpty();
        
        var session = _sut.TryGetSession(code);
        session!.Players.Should().ContainKey(2).WhoseValue.Should().Be("SecondName");
        session.Players.Should().HaveCount(2);
    }

    [Fact]
    public void TryAddPlayer_WithInvalidCode_ReturnsFalse()
    {
        // Act
        var result = _sut.TryAddPlayer("NONE", 1, "Player", out var errorMessage);

        // Assert
        result.Should().BeFalse();
        errorMessage.Should().Be("Game session not found.");
    }

    [Fact]
    public void TryAddPlayer_WhenSessionFull_ReturnsFalse()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        // Add 4 more players (total 5)
        _sut.TryAddPlayer(code, 2, "P2", out _);
        _sut.TryAddPlayer(code, 3, "P3", out _);
        _sut.TryAddPlayer(code, 4, "P4", out _);
        _sut.TryAddPlayer(code, 5, "P5", out _);

        // Act
        var result = _sut.TryAddPlayer(code, 6, "P6", out var errorMessage);

        // Assert
        result.Should().BeFalse();
        errorMessage.Should().Be("Game session is full.");
    }

    [Fact]
    public void TryAddPlayer_WhenRejoining_ReturnsTrue()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        // Fill the session
        _sut.TryAddPlayer(code, 2, "P2", out _);
        _sut.TryAddPlayer(code, 3, "P3", out _);
        _sut.TryAddPlayer(code, 4, "P4", out _);
        _sut.TryAddPlayer(code, 5, "P5", out _);

        // Act - Re-add an existing player (idempotency/reconnection)
        var result = _sut.TryAddPlayer(code, 3, "P3-UpdatedNickname", out var errorMessage);

        // Assert
        result.Should().BeTrue();
        errorMessage.Should().BeEmpty();
        var snapshot = _sut.GetLobbySnapshot(code);
        snapshot!.Players.Should().HaveCount(5);
        snapshot.Players.Should().Contain(p => p.UserId == 3 && p.Nickname == "P3-UpdatedNickname");
    }

    [Fact]
    public void StartNextRound_PicksLetterAndUpdatesState()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });

        // Act
        var (letter, _, gameOverReason) = _sut.StartNextRound(code);

        // Assert
        gameOverReason.Should().BeNull();
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
        var code = _sut.CreateSession(1, "Host", 100, 60, new List<int> { 1 });
        const string alphabet = "ABCDEFGHJKLMNPRSTUWXY"; // 21 letters
        for (int i = 0; i < alphabet.Length; i++)
        {
            _sut.StartNextRound(code);
        }

        // Act
        var (letter, _, gameOverReason) = _sut.StartNextRound(code);

        // Assert
        letter.Should().BeNull();
        gameOverReason.Should().Be("No more letters available.");

    }

    [Fact]
    public void LockRound_UpdatesStateAndClearsTimer()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
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
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
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
    public void TrySubmitAnswers_AcceptsWithinGracePeriod()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        _sut.StartNextRound(code);
        _sut.LockRound(code);
        
        // Advance time by 1 second (Grace period is 3s)
        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        
        var answers = new Dictionary<int, string> { { 1, "Apple" } };
        
        // Act
        var result = _sut.TrySubmitAnswers(code, 2, answers);
        
        // Assert
        result.Should().BeTrue();
        var session = _sut.TryGetSession(code);
        session!.CurrentRoundAnswers.Should().ContainKey(2);
    }


    [Fact]
    public void TrySubmitAnswers_RejectsAfterGracePeriod()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        _sut.StartNextRound(code);
        _sut.LockRound(code);
        
        // Advance time by 4 seconds (Grace period is 3s)
        _timeProvider.Advance(TimeSpan.FromSeconds(4));
        
        var answers = new Dictionary<int, string> { { 1, "Apple" } };
        
        // Act
        var result = _sut.TrySubmitAnswers(code, 2, answers);
        
        // Assert
        result.Should().BeFalse();
        var session = _sut.TryGetSession(code);
        session!.CurrentRoundAnswers.Should().NotContainKey(2);
    }



    [Fact]
    public void UpdateSessionSettings_UpdatesValuesCorrectly()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        var newRounds = 10;
        var newTimer = 30;
        var newCategories = new List<int> { 2, 3 };
        var newLanguage = "es";

        // Act
        _sut.UpdateSessionSettings(code, newRounds, newTimer, newCategories, newLanguage);

        // Assert
        var session = _sut.TryGetSession(code);
        session.Should().NotBeNull();
        session!.TotalRounds.Should().Be(newRounds);
        session.TimerDuration.Should().Be(newTimer);
        session.SelectedCategoryIds.Should().BeEquivalentTo(newCategories);
        session.Language.Should().Be(newLanguage);
    }

    [Fact]
    public void SubmitValidation_DuplicateSubmissions_ReturnsTrueOnlyOnce()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 3, 30, new List<int> { 1 });
        _sut.TryAddPlayer(code, 2, "Guest", out _);
        
        // Act & Assert
        // First user validates
        _sut.SubmitValidation(code, 1).Should().BeFalse(); // 1/2 validated
        
        // Second user validates (reaches threshold)
        _sut.SubmitValidation(code, 2).Should().BeTrue();  // 2/2 validated -> triggers scoring
        
        // Second user (or any user) validates AGAIN
        _sut.SubmitValidation(code, 2).Should().BeFalse(); // Already met, should not return true again
        _sut.SubmitValidation(code, 1).Should().BeFalse(); 
    }

    [Fact]
    public void MarkPlayerOffline_AndOnline_UpdatesOfflinePlayersAndLobbySnapshot()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        _sut.TryAddPlayer(code, 2, "Guest", out _);

        // Act & Assert 1: Mark player offline
        _sut.MarkPlayerOffline(code, 2);
        var session = _sut.TryGetSession(code);
        session!.OfflinePlayers.Should().Contain(2);

        var snapshotOffline = _sut.GetLobbySnapshot(code);
        snapshotOffline.Should().NotBeNull();
        var guestPlayerOffline = snapshotOffline!.Players.First(p => p.UserId == 2);
        guestPlayerOffline.IsOnline.Should().BeFalse();

        // Act & Assert 2: Mark player online
        _sut.MarkPlayerOnline(code, 2);
        session.OfflinePlayers.Should().NotContain(2);

        var snapshotOnline = _sut.GetLobbySnapshot(code);
        var guestPlayerOnline = snapshotOnline!.Players.First(p => p.UserId == 2);
        guestPlayerOnline.IsOnline.Should().BeTrue();
    }

    [Fact]
    public void IsRoundAcceptingAnswers_ValidatesStates()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        
        // Before round starts: should be false
        _sut.IsRoundAcceptingAnswers(code).Should().BeFalse();

        // Round starts: should be true
        _sut.StartNextRound(code);
        _sut.IsRoundAcceptingAnswers(code).Should().BeTrue();

        // Round locked, but within 3 seconds grace: should be true
        _sut.LockRound(code);
        _sut.IsRoundAcceptingAnswers(code).Should().BeTrue();

        // After grace period (e.g. 4 seconds): should be false
        _timeProvider.Advance(TimeSpan.FromSeconds(4));
        _sut.IsRoundAcceptingAnswers(code).Should().BeFalse();
    }

    [Fact]
    public void ValidateSessionSettings_ValidSettings_NoException()
    {
        // Act & Assert
        Action act = () => _sut.ValidateSessionSettings(5, 60, new List<int> { 1 });
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateSessionSettings_InvalidCategories_ThrowsArgumentException()
    {
        // Act & Assert
        Action actEmpty = () => _sut.ValidateSessionSettings(5, 60, new List<int>());
        actEmpty.Should().Throw<ArgumentException>().WithMessage("At least one category must be selected.*");

        Action actNull = () => _sut.ValidateSessionSettings(5, 60, null!);
        actNull.Should().Throw<ArgumentException>().WithMessage("At least one category must be selected.*");
    }

    [Fact]
    public void ValidateSessionSettings_InvalidRounds_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Action actLow = () => _sut.ValidateSessionSettings(0, 60, new List<int> { 1 });
        actLow.Should().Throw<ArgumentOutOfRangeException>().WithMessage("Total rounds must be between 1 and 20.*");

        Action actHigh = () => _sut.ValidateSessionSettings(21, 60, new List<int> { 1 });
        actHigh.Should().Throw<ArgumentOutOfRangeException>().WithMessage("Total rounds must be between 1 and 20.*");
    }

    [Fact]
    public void ValidateSessionSettings_InvalidTimer_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Action actLow = () => _sut.ValidateSessionSettings(5, 29, new List<int> { 1 });
        actLow.Should().Throw<ArgumentOutOfRangeException>().WithMessage("Timer duration must be between 30 and 120 seconds.*");

        Action actHigh = () => _sut.ValidateSessionSettings(5, 121, new List<int> { 1 });
        actHigh.Should().Throw<ArgumentOutOfRangeException>().WithMessage("Timer duration must be between 30 and 120 seconds.*");
    }

    [Fact]
    public void TryRemoveIfStillOffline_RemovesCorrectly()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        _sut.TryAddPlayer(code, 2, "Player2", out _);
        
        // 1. Try to remove when player is online
        _sut.TryRemoveIfStillOffline(code, 2).Should().BeFalse();
        var session = _sut.TryGetSession(code);
        session!.Players.Should().ContainKey(2);

        // 2. Mark player offline and remove
        _sut.MarkPlayerOffline(code, 2);
        _sut.TryRemoveIfStillOffline(code, 2).Should().BeTrue();
        session.Players.Should().NotContainKey(2);
        session.OfflinePlayers.Should().NotContain(2);

        // 3. Try to remove nonexistent player/session
        _sut.TryRemoveIfStillOffline("NONE", 2).Should().BeFalse();
    }

    [Fact]
    public void CheckAllAnswersSubmitted_SubtractsOfflinePlayers()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        _sut.TryAddPlayer(code, 2, "Player2", out _);
        _sut.TryAddPlayer(code, 3, "Player3", out _); // Total 3 players: 1, 2, 3

        _sut.StartNextRound(code);

        // Submit answers for 1 and 2
        _sut.TrySubmitAnswers(code, 1, new Dictionary<int, string> { { 1, "A" } });
        _sut.TrySubmitAnswers(code, 2, new Dictionary<int, string> { { 1, "B" } });

        // Quorum not met yet (2/3 answers)
        _sut.CheckAllAnswersSubmitted(code).Should().BeFalse();

        // Act: Player 3 goes offline
        _sut.MarkPlayerOffline(code, 3);

        // Assert: Quorum should now be met (2/2 active players)
        _sut.CheckAllAnswersSubmitted(code).Should().BeTrue();
    }

    [Fact]
    public void SubmitValidation_SubtractsOfflinePlayers()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        _sut.TryAddPlayer(code, 2, "Player2", out _);
        _sut.TryAddPlayer(code, 3, "Player3", out _); // Total 3 players: 1, 2, 3

        _sut.StartNextRound(code);
        _sut.LockRound(code);

        // Player 1 and Player 2 validate
        _sut.SubmitValidation(code, 1).Should().BeFalse();
        
        // Before Player 2 validates, Player 3 goes offline
        _sut.MarkPlayerOffline(code, 3);

        // Player 2 validates - this should trigger the true validation quorum (2/2 active players)
        _sut.SubmitValidation(code, 2).Should().BeTrue();
    }

    [Fact]
    public void MarkPlayerOfflineAndCheckQuorum_TipsQuorumCorrectly()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        _sut.TryAddPlayer(code, 2, "Player2", out _);
        _sut.TryAddPlayer(code, 3, "Player3", out _); // Total 3 players

        _sut.StartNextRound(code);

        // 1. Check answers quorum tipping
        _sut.TrySubmitAnswers(code, 1, new Dictionary<int, string> { { 1, "A" } });
        _sut.TrySubmitAnswers(code, 2, new Dictionary<int, string> { { 1, "B" } });

        // Mark player 3 offline. Since 2 out of 2 active players submitted, this should tip answers quorum.
        var (answersTipped, validationTipped) = _sut.MarkPlayerOfflineAndCheckQuorum(code, 3);
        answersTipped.Should().BeTrue();
        validationTipped.Should().BeFalse();

        // 2. Check validation quorum tipping
        _sut.LockRound(code);
        _sut.SubmitValidation(code, 1);

        // Bring Player 3 back online so active players is 3 again
        _sut.MarkPlayerOnline(code, 3);

        // Mark Player 2 offline. Active is now 2 (Host 1, Guest 3). Only Host 1 has validated (1/2).
        var (ansTipped2, valTipped2) = _sut.MarkPlayerOfflineAndCheckQuorum(code, 2);
        ansTipped2.Should().BeFalse(); // Already met answers quorum
        valTipped2.Should().BeFalse(); // Validation quorum not met yet (1/2)

        // Mark Player 3 offline. Active is now 1 (Host 1). Host 1 validated (1/1). Quorum tipped!
        var (ansTipped3, valTipped3) = _sut.MarkPlayerOfflineAndCheckQuorum(code, 3);
        ansTipped3.Should().BeFalse();
        valTipped3.Should().BeTrue();
    }

    [Fact]
    public void MarkPlayerOfflineAndCheckQuorum_RoundZero_ReturnsFalse()
    {
        // Arrange
        var code = _sut.CreateSession(1, "Host", 5, 60, new List<int> { 1 });
        _sut.TryAddPlayer(code, 2, "Player2", out _);

        // Act: Player 2 goes offline at round 0
        var (answersTipped, validationTipped) = _sut.MarkPlayerOfflineAndCheckQuorum(code, 2);

        // Assert: Quorum shouldn't be checked or tipped at round 0
        answersTipped.Should().BeFalse();
        validationTipped.Should().BeFalse();

        var session = _sut.TryGetSession(code);
        session!.OfflinePlayers.Should().Contain(2);
    }
}

