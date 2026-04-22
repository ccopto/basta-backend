using Basta.Server.Data;
using Basta.Server.DTOs;
using Basta.Server.Entities;
using Microsoft.EntityFrameworkCore;

namespace Basta.Server.Services;

public class ScoringService : IScoringService
{
    private readonly BastaDbContext _context;

    public ScoringService(BastaDbContext context)
    {
        _context = context;
    }

    public async Task<List<PlayerScoreDto>> CalculateAndAwardPointsAsync(string gameId, int roundNumber, char roundLetter)
    {
        // 1. Fetch all players and their answers for the round
        var gamePlayers = await _context.GamePlayers
            .Include(gp => gp.User)
            .Where(gp => gp.GameId == gameId)
            .ToListAsync();

        var roundAnswers = await _context.RoundAnswers
            .Where(a => a.GameId == gameId && a.RoundNumber == roundNumber)
            .ToListAsync();

        // 2. Identify shared vs unique answers among valid ones
        // Normalize: trim and lowercase
        var validAnswers = roundAnswers
            .Where(a => a.IsValid == true && 
                        !string.IsNullOrWhiteSpace(a.SubmittedAnswer) &&
                        a.SubmittedAnswer.Trim().StartsWith(roundLetter.ToString(), StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => a.SubmittedAnswer.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Count());

        var scores = new List<PlayerScoreDto>();

        foreach (var player in gamePlayers)
        {
            var playerAnswers = roundAnswers.Where(a => a.UserId == player.UserId).ToList();
            var answerScores = new List<AnswerScoreDto>();
            int roundScore = 0;

            foreach (var answer in playerAnswers)
            {
                int points = 0;
                bool isUnique = false;
                bool actuallyValid = false;

                if (answer.IsValid == true && 
                    !string.IsNullOrWhiteSpace(answer.SubmittedAnswer) &&
                    answer.SubmittedAnswer.Trim().StartsWith(roundLetter.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    actuallyValid = true;
                    var normalized = answer.SubmittedAnswer.Trim().ToLowerInvariant();
                    if (validAnswers.TryGetValue(normalized, out var count))
                    {
                        if (count == 1)
                        {
                            points = 10;
                            isUnique = true;
                        }
                        else
                        {
                            points = 5;
                            isUnique = false;
                        }
                    }
                }

                answer.PointsAwarded = points;
                roundScore += points;

                answerScores.Add(new AnswerScoreDto(
                    answer.CategoryId,
                    answer.SubmittedAnswer,
                    actuallyValid,
                    points,
                    isUnique));
            }

            player.CumulativeScore += roundScore;
            
            scores.Add(new PlayerScoreDto(
                player.UserId,
                player.User?.Nickname ?? "Unknown",
                roundScore,
                player.CumulativeScore,
                answerScores));
        }

        // 3. Persist all updates (RoundAnswers.PointsAwarded and GamePlayers.CumulativeScore)
        await _context.SaveChangesAsync();

        return scores;
    }
}
