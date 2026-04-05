namespace Basta.Server.Entities;

public class User
{
    public int UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string PreferredLanguage { get; set; } = "en";

    // Navigation properties
    public ICollection<Game> HostedGames { get; set; } = new List<Game>();
    public ICollection<GamePlayer> Participations { get; set; } = new List<GamePlayer>();
}
