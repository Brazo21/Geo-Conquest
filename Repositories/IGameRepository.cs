using GeoConquest.Models.Domain;

namespace GeoConquest.Repositories;

public sealed record GameQuery(
    string? OwnerUserId = null,
    GameStatus? Status = null,
    string? Search = null,
    int Limit = 50);

public interface IGameRepository
{
    Task<GameSession?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameSession>> SearchAsync(GameQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameSession>> ListForStatsAsync(DateTime? createdSinceUtc = null, int limit = 2000,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameSession>> ListFullAsync(string? ownerUserId, IReadOnlyCollection<string>? ids = null,
        int limit = 40, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, int>> CountByOwnersAsync(IReadOnlyCollection<string> ownerUserIds,
        CancellationToken cancellationToken = default);

    Task<long> DeleteByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default);

    Task InsertAsync(GameSession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(GameSession session, CancellationToken cancellationToken = default);
}
