namespace Basta.Server.Entities;

public class GamePlayer
{
    public int GamePlayerId { get; set; }
    public string GameId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public int CumulativeScore { get; set; } = 0;

    // Navigation properties
    public Game Game { get; set; } = null!;
    public User User { get; set; } = null!;
}
