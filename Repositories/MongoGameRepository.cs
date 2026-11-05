using GeoConquest.Data;
using GeoConquest.Models.Domain;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GeoConquest.Repositories;

/// <summary>
/// MongoDB implementation of <see cref="IGameRepository"/>. Each game is one document,
/// so every operation is a single round-trip and is atomic at the document level.
/// </summary>
public sealed class MongoGameRepository : IGameRepository
{
    private readonly IMongoCollection<GameSession> _games;
    private readonly ILogger<MongoGameRepository> _logger;

    public MongoGameRepository(MongoDbContext context, ILogger<MongoGameRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        _games = context.Games;
        _logger = logger;
    }

    public async Task<GameSession?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return await _games
            .Find(g => g.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GameSession>> SearchAsync(GameQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var f = Builders<GameSession>.Filter;
        var filter = f.Empty;

        if (!string.IsNullOrWhiteSpace(query.OwnerUserId))
            filter &= f.Eq(g => g.OwnerUserId, query.OwnerUserId);

        if (query.Status is not null)
            filter &= f.Eq(g => g.Status, query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Escape user input so it is matched literally (no regex injection).
            var pattern = new BsonRegularExpression(Regex.Escape(query.Search.Trim()), "i");
            filter &= f.Or(
                f.Regex("players.displayName", pattern),
                f.Regex("countries.name", pattern));
        }

        int limit = Math.Clamp(query.Limit, 1, 100);

        var games = await _games
            .Find(filter)
            .Project<GameSession>(Builders<GameSession>.Projection.Exclude("events"))
            .SortByDescending(g => g.UpdatedAtUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);

        return games.AsReadOnly();
    }

    public async Task<IReadOnlyList<GameSession>> ListForStatsAsync(DateTime? createdSinceUtc = null, int limit = 2000,
        CancellationToken cancellationToken = default)
    {
        var f = Builders<GameSession>.Filter;
        var filter = createdSinceUtc is null ? f.Empty : f.Gte(g => g.CreatedAtUtc, createdSinceUtc.Value);

        var games = await _games
            .Find(filter)
            .Project<GameSession>(Builders<GameSession>.Projection.Slice("events", 1))
            .SortByDescending(g => g.UpdatedAtUtc)
            .Limit(Math.Clamp(limit, 1, 5000))
            .ToListAsync(cancellationToken);

        return games.AsReadOnly();
    }

    public async Task<IReadOnlyList<GameSession>> ListFullAsync(string? ownerUserId, IReadOnlyCollection<string>? ids = null,
        int limit = 40, CancellationToken cancellationToken = default)
    {
        var f = Builders<GameSession>.Filter;
        var filter = f.Empty;
        if (!string.IsNullOrWhiteSpace(ownerUserId))
            filter &= f.Eq(g => g.OwnerUserId, ownerUserId);
        if (ids is not null)
            filter &= f.In(g => g.Id, ids);

        var games = await _games
            .Find(filter)
            .SortByDescending(g => g.UpdatedAtUtc)
            .Limit(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);

        return games.AsReadOnly();
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByOwnersAsync(IReadOnlyCollection<string> ownerUserIds,
        CancellationToken cancellationToken = default)
    {
        if (ownerUserIds is null || ownerUserIds.Count == 0)
            return new Dictionary<string, int>();

        // One grouped query for the whole page of accounts, not one query per account.
        var rows = await _games.Aggregate()
            .Match(Builders<GameSession>.Filter.In(g => g.OwnerUserId, ownerUserIds))
            .Group(g => g.OwnerUserId, grp => new { Owner = grp.Key, Count = grp.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Owner, r => r.Count);
    }

    public async Task<long> DeleteByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return 0;

        var result = await _games.DeleteManyAsync(g => g.OwnerUserId == ownerUserId, cancellationToken);
        _logger.LogInformation("Deleted {Count} games owned by user {UserId}", result.DeletedCount, ownerUserId);
        return result.DeletedCount;
    }

    public async Task InsertAsync(GameSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.MarkSaved(); // Version 0 → 1, stamps UpdatedAtUtc

        try
        {
            await _games.InsertOneAsync(session, cancellationToken: cancellationToken);
            _logger.LogInformation("Created game {GameId} for user {UserId}", session.Id, session.OwnerUserId);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException($"A game with id {session.Id} already exists.", ex);
        }
    }

    public async Task UpdateAsync(GameSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        long expectedVersion = session.Version;
        session.MarkSaved();

        // Optimistic concurrency: only replace the document if nobody saved since we loaded it.
        var filter = Builders<GameSession>.Filter.And(
            Builders<GameSession>.Filter.Eq(g => g.Id, session.Id),
            Builders<GameSession>.Filter.Eq(g => g.Version, expectedVersion));

        var result = await _games.ReplaceOneAsync(
            filter,
            session,
            new ReplaceOptions { IsUpsert = false },
            cancellationToken);

        if (result.MatchedCount == 0)
        {
            _logger.LogWarning("Concurrency conflict on game {GameId} at version {Version}", session.Id, expectedVersion);
            throw new ConcurrencyConflictException(session.Id, expectedVersion);
        }
    }
}
