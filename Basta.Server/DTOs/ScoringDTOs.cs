namespace Basta.Server.DTOs;

public record PlayerScoreDto(
    int UserId, 
    string Nickname, 
    int RoundScore, 
    int CumulativeScore, 
    List<AnswerScoreDto> Answers);

public record AnswerScoreDto(
    int CategoryId, 
    string Answer, 
    bool IsValid, 
    int Points, 
    bool IsUnique);

public record RoundAnswersDto(
    List<PlayerAnswersDto> Players);

public record PlayerAnswersDto(
    int UserId,
    string Nickname,
    Dictionary<int, string> Answers);

public record LeaderboardDto(
    string Reason, 
    List<LeaderboardPlayerDto> Players);

public record LeaderboardPlayerDto(
    int UserId, 
    string Nickname, 
    int CumulativeScore, 
    int Rank);

