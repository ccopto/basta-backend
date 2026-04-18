using System.ComponentModel.DataAnnotations;

namespace Basta.Server.Entities;

public class RoundAnswer
{
    public int RoundAnswerId { get; set; }

    [Required]
    [MaxLength(4)]
    public string GameId { get; set; } = string.Empty;

    public int RoundNumber { get; set; }

    public int UserId { get; set; }

    public int CategoryId { get; set; }

    [MaxLength(200)]
    public string SubmittedAnswer { get; set; } = string.Empty;

    // null = pending validation, true = valid, false = invalid
    public bool? IsValid { get; set; }

    public int PointsAwarded { get; set; } = 0;

    // Navigation properties
    public Game? Game { get; set; }
    public User? User { get; set; }
}
