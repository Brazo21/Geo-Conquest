namespace GeoConquest.Repositories;

/// <summary>
/// Raised when a game was modified by another request between load and save
/// (e.g. a double-clicked "Roll" button or two open tabs). The caller should reload and retry.
/// </summary>
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
