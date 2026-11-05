using GeoConquest.Models.Domain;

namespace GeoConquest.Services;

/// <summary>
/// The only entry point controllers use to change a game. Every method loads the game,
/// checks the caller is its owner and that it is their turn, applies the rules, runs any
/// NPC turns that follow, and saves — so the server validates every action.
/// Throws <see cref="GameNotFoundException"/>, <see cref="UnauthorizedAccessException"/>
/// (not your game) or <see cref="DomainException"/> (illegal move).
/// </summary>
public interface IGameEngineService
{
    IReadOnlyList<CountryInfo> GetSelectableCountries();

    Task<GameSession> CreateGameAsync(string userId, string displayName, string countryCode,
        int npcCount = GameEngineService.DefaultNpcCount, CancellationToken cancellationToken = default);

    /// <summary>Read-only load for viewing/spectating. Anyone may call this.</summary>
    Task<GameSession> GetGameAsync(string gameId, CancellationToken cancellationToken = default);

    Task<MoveOutcome> PlaceReinforcementsAsync(string gameId, string userId, string countryCode, int amount,
        CancellationToken cancellationToken = default);

    Task<MoveOutcome> TransferAsync(string gameId, string userId, string fromCode, string toCode, int amount,
        CancellationToken cancellationToken = default);

    Task<MoveOutcome> StartAttackAsync(string gameId, string userId, string fromCode, string toCode,
        CancellationToken cancellationToken = default);

    Task<MoveOutcome> RollAsync(string gameId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Stops the attack. Per the rules this also ends the turn.</summary>
    Task<MoveOutcome> RetreatAsync(string gameId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Ends (or skips) the turn, then plays every NPC turn until it is the human's turn again.</summary>
    Task<MoveOutcome> EndTurnAsync(string gameId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Offers a pact to an NPC. The NPC accepts or refuses in character (one proposal per turn).</summary>
    Task<MoveOutcome> ProposeAllianceAsync(string gameId, string userId, string partnerPlayerId,
        CancellationToken cancellationToken = default);

    /// <summary>Breaks the human's pact early. The ally remembers the betrayal.</summary>
    Task<MoveOutcome> BreakAllianceAsync(string gameId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a game for viewing and, if the human left a battle or turn idle past the limit, resolves it first
    /// (anti-cheat: closing the browser mid-attack can't freeze or undo the game). Returns the message if it did.
    /// </summary>
    Task<(GameSession Session, string? TimeoutMessage)> GetGameEnforcingTimeoutsAsync(string gameId,
        CancellationToken cancellationToken = default);

    /// <summary>Odds preview for the UI (e.g. "WIN ODDS 24%").</summary>
    double CalculateWinOdds(GameSession session, string fromCode, string toCode);
}

/// <summary>
/// Result of one move. <see cref="ReplayFromSequence"/> is the last event the player had already seen,
/// so the page can replay everything after it (the move itself, then the NPC turns) step by step.
/// </summary>
public sealed record MoveOutcome(GameSession Session, long ReplayFromSequence);

public sealed class GameNotFoundException : Exception
{
    public GameNotFoundException(string gameId) : base($"Game '{gameId}' was not found.")
    {
        GameId = gameId;
    }

    public string GameId { get; }
}

/// <summary>
/// The human's battle or turn was idle past the anti-cheat limit and was resolved by the server before
/// their new move could run. <see cref="ReplayFromSequence"/> lets the page replay what happened.
/// </summary>
public sealed class TurnTimedOutException : Exception
{
    public TurnTimedOutException(string message, long replayFromSequence) : base(message)
    {
        ReplayFromSequence = replayFromSequence;
    }

    public long ReplayFromSequence { get; }
}
