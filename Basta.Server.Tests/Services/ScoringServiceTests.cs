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

    [Fact]
    public async Task GetLeaderboardAsync_TiedScores_AssignsSameRankAndSkips()
    {
        // Arrange
        var gameId = "LEAD";
        var host = new User { UserId = 10, Nickname = "P1" };
        _context.Users.AddRange(
            host,
            new User { UserId = 20, Nickname = "P2" },
            new User { UserId = 30, Nickname = "P3" },
            new User { UserId = 40, Nickname = "P4" }
        );

        _context.Games.Add(new Game
        {
            GameId = gameId,
            HostUserId = 10,
            TotalRounds = 3,
            TimerDuration = 60
        });

        _context.GamePlayers.AddRange(
            new GamePlayer { GameId = gameId, UserId = 10, CumulativeScore = 100 },
            new GamePlayer { GameId = gameId, UserId = 20, CumulativeScore = 100 },
            new GamePlayer { GameId = gameId, UserId = 30, CumulativeScore = 80 },
            new GamePlayer { GameId = gameId, UserId = 40, CumulativeScore = 70 }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetLeaderboardAsync(gameId, "Test Reason");

        // Assert
        result.Reason.Should().Be("Test Reason");
        result.Players.Should().HaveCount(4);
        
        // P1 and P2 should be Rank 1 (order between P1/P2 not strictly defined, but both Rank 1)
        result.Players.Where(p => p.Rank == 1).Should().HaveCount(2);
        result.Players.First(p => p.Rank == 1).CumulativeScore.Should().Be(100);
        
        // P3 should be Rank 3 (skipping 2)
        result.Players.First(p => p.Rank == 3).CumulativeScore.Should().Be(80);
        
        // P4 should be Rank 4
        result.Players.First(p => p.Rank == 4).CumulativeScore.Should().Be(70);
    }

    public void Dispose()
    {
        _connection.Close();
        _context.Dispose();
    }
}
