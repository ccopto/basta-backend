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
