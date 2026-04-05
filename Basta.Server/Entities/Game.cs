namespace Basta.Server.Entities;

public class Game
{
    public string GameId { get; set; } = string.Empty; // 4-letter code
    public int HostUserId { get; set; }
    public int TotalRounds { get; set; }
    public int TimerDuration { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User Host { get; set; } = null!;
    public ICollection<GamePlayer> Players { get; set; } = new List<GamePlayer>();
}
