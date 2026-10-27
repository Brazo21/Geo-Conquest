using GeoConquest.Models.Domain;

namespace GeoConquest.Repositories;

/// <summary>
/// Persistence contract for the GameSession aggregate. Services depend on this
/// interface only, so MongoDB can be swapped or mocked in tests.
/// </summary>
public interface IGameRepository
{
    /// <summary>Loads the whole game (players + countries) in one query, or null if it doesn't exist.</summary>
    Task<GameSession?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Saves a brand-new game.</summary>
    Task InsertAsync(GameSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the stored game with this version.
    /// Throws <see cref="ConcurrencyConflictException"/> if someone else saved it first.
    /// </summary>
    Task UpdateAsync(GameSession session, CancellationToken cancellationToken = default);
}
