using Basta.Server.Models;
using Basta.Server.DTOs;

namespace Basta.Server.Services;

public interface IGameSessionService
{
    string CreateSession(int hostUserId, int totalRounds, int timerDuration, List<int> categoryIds);
    GameSession? TryGetSession(string code);

    /// <summary>
    /// Removes an in-memory session by code. Used to roll back a session that was
    /// registered before a database transaction failed.
    /// </summary>
    bool RemoveSession(string code);

    /// <summary>
    /// Attempts to add a player to an in-memory session.
    /// Validates session capacity and unique nicknames. Error message is populated on failure.
    /// </summary>
    bool TryAddPlayer(string code, int userId, string nickname, out string errorMessage);

    /// <summary>
    /// Constructs a thread-safe snapshot of the current lobby state for broadcasting.
    /// </summary>
    LobbySnapshot? GetLobbySnapshot(string code);

    /// <summary>
    /// Removes a player from the in-memory session tracking.
    /// </summary>
    void RemovePlayer(string code, int userId);

    /// <summary>
    /// Starts the next round: picks a new letter, increments round counter,
    /// clears previous answers, and marks the round as active.
    /// Returns the selected letter and a CancellationToken for the round timer.
    /// </summary>
    (char? letter, CancellationToken cancellationToken) StartNextRound(string code);

    /// <summary>
    /// Marks the round as locked/stopped.
    /// </summary>
    void LockRound(string code);

    /// <summary>
    /// Records a player's answers for the current round. 
    /// Returns false if the round is already locked.
    /// </summary>
    bool TrySubmitAnswers(string code, int userId, Dictionary<int, string> answers);

    /// <summary>
    /// Updates the game session settings (rounds, timer, categories).
    /// </summary>
    void UpdateSessionSettings(string code, int totalRounds, int timerDuration, List<int> categoryIds);
}

