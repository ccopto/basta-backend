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

    // We can hold connected players in future stories.
    public Dictionary<int, string> Players { get; set; } = new();
    
    // Letters that have been generated in past rounds
    public HashSet<char> UsedLetters { get; set; } = new();

    // Category IDs selected by the host for this session
    public List<int> SelectedCategoryIds { get; set; } = new();
}

