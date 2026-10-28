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
