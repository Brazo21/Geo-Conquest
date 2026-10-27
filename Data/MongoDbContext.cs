using GeoConquest.Configuration;
using GeoConquest.Models.Domain;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GeoConquest.Data;

/// <summary>
/// Single access point to the Atlas database. Registered as a singleton because
/// IMongoClient / IMongoCollection are thread-safe and pool their connections.
/// </summary>
public sealed class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IMongoClient client, IOptions<MongoDbSettings> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        var settings = options.Value;

        _database = client.GetDatabase(settings.DatabaseName);
        Games = _database.GetCollection<GameSession>(settings.GamesCollectionName);
    }

    public IMongoCollection<GameSession> Games { get; }

    /// <summary>Round-trips to Atlas; throws if the cluster is unreachable or credentials are wrong.</summary>
    public Task PingAsync(CancellationToken cancellationToken = default) =>
        _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);

    /// <summary>Creates the indexes the app's list pages rely on. Safe to run on every startup.</summary>
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var keys = Builders<GameSession>.IndexKeys;

        var indexes = new[]
        {
            // "My games" list: games for one user, newest first.
            new CreateIndexModel<GameSession>(
                keys.Ascending(g => g.OwnerUserId).Descending(g => g.UpdatedAtUtc),
                new CreateIndexOptions { Name = "ix_owner_updated" }),

            // "Live games" list for spectators: in-progress games, most recently active first.
            new CreateIndexModel<GameSession>(
                keys.Ascending(g => g.Status).Descending(g => g.UpdatedAtUtc),
                new CreateIndexOptions { Name = "ix_status_updated" })
        };

        await Games.Indexes.CreateManyAsync(indexes, cancellationToken);
    }
}
