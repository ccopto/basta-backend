using System.ComponentModel.DataAnnotations;

namespace Basta.Server.DTOs;

public class CreateGameRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(50)]
    public string HostNickname { get; set; } = string.Empty;

    public string PreferredLanguage { get; set; } = "en";
    
    [Range(1, 20)]
    public int TotalRounds { get; set; } = 5;

    [Range(30, 120)]
    public int TimerDuration { get; set; } = 60;
}

public class CreateGameResponse
{
    public string GameCode { get; set; } = string.Empty;
    public int HostUserId { get; set; }
}
