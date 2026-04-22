using Basta.Server.Data;
using Basta.Server.Entities;
using Basta.Server.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Basta.Server.Tests.Services;

public class ScoringServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BastaDbContext _context;
    private readonly ScoringService _sut;

    public ScoringServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BastaDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new BastaDbContext(options);
        _context.Database.EnsureCreated();

        _sut = new ScoringService(_context);
    }

    [Fact]
    public async Task CalculateAndAwardPointsAsync_StandardRules_AwardsCorrectPoints()
    {
        // Arrange
        var gameId = "TEST";
        var roundNumber = 1;
        var roundLetter = 'A';

        // 1. Create Users
        var user1 = new User { UserId = 1, Nickname = "Alice" };
        var user2 = new User { UserId = 2, Nickname = "Bob" };
        _context.Users.AddRange(user1, user2);

        // 1.5 Create Game
        var game = new Game
        {
            GameId = gameId,
            HostUserId = 1,
            TotalRounds = 3,
            TimerDuration = 60
        };
        _context.Games.Add(game);

        // 2. Create GamePlayers

        _context.GamePlayers.AddRange(
            new GamePlayer { GameId = gameId, UserId = 1, CumulativeScore = 0 },
            new GamePlayer { GameId = gameId, UserId = 2, CumulativeScore = 0 }
        );

        // 3. Create Answers
        _context.RoundAnswers.AddRange(
            // User 1: Unique valid (10), Shared valid (5), Invalid (0)
            new RoundAnswer { GameId = gameId, RoundNumber = roundNumber, UserId = 1, CategoryId = 1, SubmittedAnswer = "Apple", IsValid = true },
            new RoundAnswer { GameId = gameId, RoundNumber = roundNumber, UserId = 1, CategoryId = 2, SubmittedAnswer = "Apricot", IsValid = true },
            new RoundAnswer { GameId = gameId, RoundNumber = roundNumber, UserId = 1, CategoryId = 3, SubmittedAnswer = "Artichoke", IsValid = false },

            // User 2: Unique valid (10), Shared valid (5), Wrong Letter (0)
            new RoundAnswer { GameId = gameId, RoundNumber = roundNumber, UserId = 2, CategoryId = 1, SubmittedAnswer = "Avocado", IsValid = true },
            new RoundAnswer { GameId = gameId, RoundNumber = roundNumber, UserId = 2, CategoryId = 2, SubmittedAnswer = "apricot ", IsValid = true }, // Shared with Alice
            new RoundAnswer { GameId = gameId, RoundNumber = roundNumber, UserId = 2, CategoryId = 3, SubmittedAnswer = "Cherry", IsValid = true }   // Starts with C, should be 0

        );

        await _context.SaveChangesAsync();

        // Act
        var results = await _sut.CalculateAndAwardPointsAsync(gameId, roundNumber, roundLetter);

        // Assert
        var alice = results.First(r => r.UserId == 1);
        var bob = results.First(r => r.UserId == 2);

        // Alice: Apple(10) + Apricot(5) + Artichoke(0) = 15
        alice.RoundScore.Should().Be(15);
        alice.CumulativeScore.Should().Be(15);
        alice.Answers.First(a => a.CategoryId == 1).Points.Should().Be(10); // Apple
        alice.Answers.First(a => a.CategoryId == 2).Points.Should().Be(5);  // Apricot
        alice.Answers.First(a => a.CategoryId == 3).Points.Should().Be(0);  // Artichoke (IsValid=false)

        // Bob: Avocado(10) + apricot(5) + Cherry(0) = 15
        bob.RoundScore.Should().Be(15);
        bob.CumulativeScore.Should().Be(15);
        bob.Answers.First(a => a.CategoryId == 1).Points.Should().Be(10); // Avocado
        bob.Answers.First(a => a.CategoryId == 2).Points.Should().Be(5);  // apricot
        bob.Answers.First(a => a.CategoryId == 3).Points.Should().Be(0);  // Cherry (Wrong letter)

    }

    public void Dispose()
    {
        _connection.Close();
        _context.Dispose();
    }
}
