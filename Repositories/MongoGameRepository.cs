using GeoConquest.Data;
using GeoConquest.Models.Domain;
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
