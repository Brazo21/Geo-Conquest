using GeoConquest.Models.Domain;

namespace GeoConquest.Repositories;

/// <summary>Filters for game lists. All parts are optional and combine with AND.</summary>
public sealed record GameQuery(
    string? OwnerUserId = null,
    GameStatus? Status = null,
    string? Search = null,
    int Limit = 50);

/// <summary>
/// Persistence contract for the GameSession aggregate. Services depend on this
/// interface only, so MongoDB can be swapped or mocked in tests.
/// </summary>
public interface IGameRepository
{
    /// <summary>Loads the whole game (players, countries, event log) in one query, or null if it doesn't exist.</summary>
    Task<GameSession?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists games, newest activity first. <see cref="GameQuery.Search"/> matches a commander's name
    /// or a country name (case-insensitive). The event log is left out to keep lists fast.
    /// </summary>
    Task<IReadOnlyList<GameSession>> SearchAsync(GameQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight load of many games for platform statistics: everything except the event log,
    /// apart from the first event (needed to recover older games' starting country). Read-only: never save these.
    /// </summary>
    Task<IReadOnlyList<GameSession>> ListForStatsAsync(DateTime? createdSinceUtc = null, int limit = 2000,
        CancellationToken cancellationToken = default);

    /// <summary>Full games (with event logs), newest first. Used for personal analytics and replays.</summary>
    Task<IReadOnlyList<GameSession>> ListFullAsync(string? ownerUserId, IReadOnlyCollection<string>? ids = null,
        int limit = 40, CancellationToken cancellationToken = default);

    /// <summary>How many games each of these users owns (users with none are left out).</summary>
    Task<IReadOnlyDictionary<string, int>> CountByOwnersAsync(IReadOnlyCollection<string> ownerUserIds,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every game a user owns (account deletion / POPIA erasure). Returns how many were removed.</summary>
    Task<long> DeleteByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Saves a brand-new game.</summary>
    Task InsertAsync(GameSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the stored game with this version.
    /// Throws <see cref="ConcurrencyConflictException"/> if someone else saved it first.
    /// </summary>
    Task UpdateAsync(GameSession session, CancellationToken cancellationToken = default);
}
