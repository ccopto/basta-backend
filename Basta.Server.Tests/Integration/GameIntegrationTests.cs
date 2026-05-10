using Basta.Server.Data;
using Basta.Server.Entities;
using Basta.Server.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Basta.Server.Tests.Integration;

public class GameIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BastaDbContext _context;
    private readonly GameSessionService _sessionService;
    private readonly GameOperationService _operationService;
    private readonly ScoringService _scoringService;
    private readonly Mock<IDictionaryService> _dictServiceMock;

    public GameIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BastaDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new BastaDbContext(options);
        _context.Database.EnsureCreated();

        var loggerMock = new Mock<ILogger<GameSessionService>>();
        _sessionService = new GameSessionService(loggerMock.Object);

        // Dictionary service that accepts all words for integration tests
        _dictServiceMock = new Mock<IDictionaryService>();
        _dictServiceMock.Setup(d => d.IsValidWord(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CategoryValidationType>()))
                        .Returns(true);

        _operationService = new GameOperationService(_context, _sessionService, _dictServiceMock.Object);
        _scoringService = new ScoringService(_context);
    }

    [Fact]
    public async Task FivePlayerLimit_EnforcedCorrectly()
    {
        // 1. Create Game (Host)
        var createResult = await _operationService.CreateGameAsync("Host", "en", "en", 3, 60, new List<int> { 1 });
        var code = createResult.GameCode;

        // 2. Join 4 more players
        for (int i = 2; i <= 5; i++)
        {
            await _operationService.JoinGameAsync(code, $"Player{i}", "en");
        }

        // 3. Attempt to join 6th player
        var act = async () => await _operationService.JoinGameAsync(code, "Player6", "en");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Game session is full.");
        
        var session = _sessionService.TryGetSession(code);
        session!.Players.Count.Should().Be(5);
    }

    [Fact]
    public async Task FullGameLoop_Integration()
    {
        // 1. Create & Join
        var createResult = await _operationService.CreateGameAsync("Host", "en", "en", 1, 60, new List<int> { 1 });
        var code = createResult.GameCode;
        var guestResult = await _operationService.JoinGameAsync(code, "Guest", "en");

        // 2. Start Round
        var (letter, _, _) = _sessionService.StartNextRound(code);
        letter.Should().NotBeNull();

        // 3. Submit Answers
        var hostAnswers = new Dictionary<int, string> { { 1, $"{letter}Host" } };
        var guestAnswers = new Dictionary<int, string> { { 1, $"{letter}Guest" } };

        await _operationService.SubmitAnswersAsync(code, 1, createResult.HostUserId, hostAnswers);
        await _operationService.SubmitAnswersAsync(code, 1, guestResult.UserId, guestAnswers);

        // 4. Submit Validation (Key is CategoryId)
        await _operationService.UpdateValidationAsync(code, 1, createResult.HostUserId, new Dictionary<int, bool> { { 1, true } });
        await _operationService.UpdateValidationAsync(code, 1, guestResult.UserId, new Dictionary<int, bool> { { 1, true } });

        // 5. Calculate Points
        var scores = await _scoringService.CalculateAndAwardPointsAsync(code, 1, letter!.Value);

        // Full integration: dict service returns true for all, so IsValid = true and DictionaryValid = true.
        // Scores should use the cascade rule: DictionaryValid=true → auto-accepted.
        scores.Should().HaveCount(2);
        scores.Should().Contain(s => s.UserId == createResult.HostUserId && s.RoundScore == 10);
        scores.Should().Contain(s => s.UserId == guestResult.UserId && s.RoundScore == 10);

        // 6. Check DB persistence for total scores
        var hostPlayer = await _context.GamePlayers.FirstAsync(p => p.UserId == createResult.HostUserId);
        hostPlayer.CumulativeScore.Should().Be(10);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
