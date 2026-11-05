using GeoConquest.Models.Domain;

namespace GeoConquest.Services;

public interface IGameEngineService
{
    IReadOnlyList<CountryInfo> GetSelectableCountries();

    Task<GameSession> CreateGameAsync(string userId, string displayName, string countryCode,
        int npcCount = GameEngineService.DefaultNpcCount, CancellationToken cancellationToken = default);

    Task<GameSession> GetGameAsync(string gameId, CancellationToken cancellationToken = default);

    Task<MoveOutcome> PlaceReinforcementsAsync(string gameId, string userId, string countryCode, int amount,
        CancellationToken cancellationToken = default);

    Task<MoveOutcome> TransferAsync(string gameId, string userId, string fromCode, string toCode, int amount,
        CancellationToken cancellationToken = default);

    Task<MoveOutcome> StartAttackAsync(string gameId, string userId, string fromCode, string toCode,
        CancellationToken cancellationToken = default);

    Task<MoveOutcome> RollAsync(string gameId, string userId, CancellationToken cancellationToken = default);

    Task<MoveOutcome> RetreatAsync(string gameId, string userId, CancellationToken cancellationToken = default);

    Task<MoveOutcome> EndTurnAsync(string gameId, string userId, CancellationToken cancellationToken = default);

    Task<MoveOutcome> ProposeAllianceAsync(string gameId, string userId, string partnerPlayerId,
        CancellationToken cancellationToken = default);

    Task<MoveOutcome> BreakAllianceAsync(string gameId, string userId, CancellationToken cancellationToken = default);

    Task<(GameSession Session, string? TimeoutMessage)> GetGameEnforcingTimeoutsAsync(string gameId,
        CancellationToken cancellationToken = default);

    double CalculateWinOdds(GameSession session, string fromCode, string toCode);
}

public sealed record MoveOutcome(GameSession Session, long ReplayFromSequence);

public sealed class GameNotFoundException : Exception
{
    public GameNotFoundException(string gameId) : base($"Game '{gameId}' was not found.")
    {
        GameId = gameId;
    }

    public string GameId { get; }
}

public sealed class TurnTimedOutException : Exception
{
    public TurnTimedOutException(string message, long replayFromSequence) : base(message)
    {
        ReplayFromSequence = replayFromSequence;
    }

    public long ReplayFromSequence { get; }
}
