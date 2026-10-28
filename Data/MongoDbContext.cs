using GeoConquest.Configuration;
using GeoConquest.Models.Domain;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GeoConquest.Data;

/// <summary>
/// Single access point to the Atlas database. Registered as a singleton because
/// IMongoClient / IMongoCollection are thread-safe and pool their connections.
/// Two collections: "games" (one document per game) and "users" (accounts).
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
        Users = _database.GetCollection<UserAccount>(settings.UsersCollectionName);
    }

    public IMongoCollection<GameSession> Games { get; }

    public IMongoCollection<UserAccount> Users { get; }

    /// <summary>Round-trips to Atlas; throws if the cluster is unreachable or credentials are wrong.</summary>
    public Task PingAsync(CancellationToken cancellationToken = default) =>
        _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);

    /// <summary>Creates the indexes the app relies on. Safe to run on every startup.</summary>
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var gameKeys = Builders<GameSession>.IndexKeys;
        await Games.Indexes.CreateManyAsync(new[]
        {
            // "My games" list: games for one user, newest first.
            new CreateIndexModel<GameSession>(
                gameKeys.Ascending(g => g.OwnerUserId).Descending(g => g.UpdatedAtUtc),
                new CreateIndexOptions { Name = "ix_owner_updated" }),

            // "Live games" list for spectators: games by status, most recently active first.
            new CreateIndexModel<GameSession>(
                gameKeys.Ascending(g => g.Status).Descending(g => g.UpdatedAtUtc),
                new CreateIndexOptions { Name = "ix_status_updated" })
        }, cancellationToken);

        var userKeys = Builders<UserAccount>.IndexKeys;
        await Users.Indexes.CreateManyAsync(new[]
        {
            // Unique indexes make duplicate sign-ups impossible, even under concurrent requests.
            new CreateIndexModel<UserAccount>(
                userKeys.Ascending(u => u.NormalizedUsername),
                new CreateIndexOptions { Name = "ux_username", Unique = true }),
            new CreateIndexModel<UserAccount>(
                userKeys.Ascending(u => u.NormalizedEmail),
                new CreateIndexOptions { Name = "ux_email", Unique = true })
        }, cancellationToken);
    }
}
