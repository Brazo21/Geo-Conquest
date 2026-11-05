namespace GeoConquest.Repositories;

public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string gameId, long expectedVersion)
        : base($"Game {gameId} was changed by another request (expected version {expectedVersion}). Reload and try again.")
    {
        GameId = gameId;
        ExpectedVersion = expectedVersion;
    }

    public string GameId { get; }

    public long ExpectedVersion { get; }
}
