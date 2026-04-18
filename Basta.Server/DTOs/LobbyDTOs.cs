namespace Basta.Server.DTOs;

public class LobbyPlayer
{
    public int UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public int Score { get; set; } // Will be used in future stories for cumulative score
    public bool IsHost { get; set; }
    public bool IsOnline { get; set; } = true; // always true in this phase
}

public class LobbySnapshot
{
    public string GameCode { get; set; } = string.Empty;
    public int TotalRounds { get; set; }
    public int TimerDuration { get; set; }
    public int HostUserId { get; set; }
    public string Language { get; set; } = string.Empty;
    public string State { get; set; } = "Lobby";
    public IEnumerable<LobbyPlayer> Players { get; set; } = Enumerable.Empty<LobbyPlayer>();
    public List<int> SelectedCategoryIds { get; set; } = new();
}
