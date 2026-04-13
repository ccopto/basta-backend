using Basta.Server.Data;
using Basta.Server.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Basta.Server.Tests.Services;

public class GameOperationServiceTests : IDisposable
{
    private readonly BastaDbContext _context;
    private readonly GameSessionService _sessionService;
    private readonly GameOperationService _sut;

    public GameOperationServiceTests()
    {
        var options = new DbContextOptionsBuilder<BastaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new BastaDbContext(options);
        _sessionService = new GameSessionService();
        _sut = new GameOperationService(_context, _sessionService);
    }

    [Fact]
    public async Task CreateGameAsync_ValidInput_CreatesUserGameAndPlayer()
    {
        // Act
        var result = await _sut.CreateGameAsync(
            nickname: "Alice",
            preferredLanguage: "en",
            totalRounds: 5,
            timerDuration: 60);

        // Assert — result shape
        result.Should().NotBeNull();
        result.GameCode.Should().NotBeNullOrWhiteSpace();
        result.GameCode.Length.Should().Be(4);
        result.HostUserId.Should().BeGreaterThan(0);

        // Assert — User was persisted
        var user = await _context.Users.FindAsync(result.HostUserId);
        user.Should().NotBeNull();
        user!.Nickname.Should().Be("Alice");
        user.PreferredLanguage.Should().Be("en");

        // Assert — Game was persisted
        var game = await _context.Games.FindAsync(result.GameCode);
        game.Should().NotBeNull();
        game!.HostUserId.Should().Be(result.HostUserId);
        game.TotalRounds.Should().Be(5);
        game.TimerDuration.Should().Be(60);

        // Assert — Host was added as a GamePlayer
        var player = await _context.GamePlayers
            .FirstOrDefaultAsync(p => p.GameId == result.GameCode && p.UserId == result.HostUserId);
        player.Should().NotBeNull();

        // Assert — In-memory session was registered
        var session = _sessionService.TryGetSession(result.GameCode);
        session.Should().NotBeNull();
        session!.HostUserId.Should().Be(result.HostUserId);
        session.TotalRounds.Should().Be(5);
        session.TimerDuration.Should().Be(60);
    }

    [Fact]
    public async Task CreateGameAsync_MultipleGames_EachGetsUniqueCode()
    {
        // Act
        var result1 = await _sut.CreateGameAsync("Alice", "en", 5, 60);
        var result2 = await _sut.CreateGameAsync("Bob", "es", 3, 30);

        // Assert
        result1.GameCode.Should().NotBe(result2.GameCode);
        result1.HostUserId.Should().NotBe(result2.HostUserId);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
