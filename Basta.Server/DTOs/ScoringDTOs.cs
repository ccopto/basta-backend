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
    bool IsUnique,
    bool? DictionaryValid);

/// <summary>
/// Per-answer payload sent during the DisplayScoring (peer-review) phase.
/// Contains dictionary validation metadata so the frontend can differentiate
/// auto-accepted answers from those requiring peer votes.
/// </summary>
public record AnswerValidationDto(
    int AnswerId,
    int CategoryId,
    string Answer,
    bool? DictionaryValid,
    bool RequiresPeerReview);

public record RoundAnswersDto(
    List<PlayerAnswersDto> Players);

public record PlayerAnswersDto(
    int UserId,
    string Nickname,
    List<AnswerValidationDto> Answers);

public record LeaderboardDto(
    string Reason, 
    List<LeaderboardPlayerDto> Players);

public record LeaderboardPlayerDto(
    int UserId, 
    string Nickname, 
    int CumulativeScore, 
    int Rank);
