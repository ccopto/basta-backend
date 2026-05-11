namespace Basta.Server.Entities;

public class Game
{
    public string GameId { get; set; } = string.Empty; // 4-letter code
    public int HostUserId { get; set; }
    public int TotalRounds { get; set; }
    public int TimerDuration { get; set; }

    /// <summary>
    /// Language code used for dictionary validation in this game session.
    /// e.g. "en" for English, "es" for Spanish.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Set by the database engine via HasDefaultValueSql("datetime('now')").
    /// Do not assign this in application code.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public User Host { get; set; } = null!;
    public ICollection<GamePlayer> Players { get; set; } = new List<GamePlayer>();
}
