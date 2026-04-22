using System.Collections.Generic;

namespace Basta.Server.Models;

public class GameSession
{
    public string Code { get; set; } = string.Empty;
    public int HostUserId { get; set; }
    public int TotalRounds { get; set; }
    public int TimerDuration { get; set; }
    public int CurrentRound { get; set; } = 0;
    public char? CurrentLetter { get; set; } 
    public bool RoundLocked { get; set; } = false;
    public DateTimeOffset? RoundLockedAt { get; set; }


    // Status to track if a round is currently running on the server
    public bool RoundActive { get; set; } = false;

    // We can hold connected players in future stories.
    public Dictionary<int, string> Players { get; set; } = new();
    
    public HashSet<char> UsedLetters { get; set; } = new();

    // Category IDs selected by the host for this session
    public List<int> SelectedCategoryIds { get; set; } = new();

    // Tracks answers per userId per category for the current round
    // Key: UserId, Value: dict of categoryId -> answer string
    public Dictionary<int, Dictionary<int, string>> CurrentRoundAnswers { get; set; } = new();

    // Tracks which players have submitted their self-validation for the current round
    public HashSet<int> PlayersValidated { get; set; } = new();
}

