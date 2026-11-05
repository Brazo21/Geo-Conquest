using GeoConquest.Configuration;
using GeoConquest.Models.Domain;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GeoConquest.Data;

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
        DataProtectionKeys = _database.GetCollection<BsonDocument>("dataProtectionKeys");
    }

    public IMongoCollection<GameSession> Games { get; }

    public IMongoCollection<UserAccount> Users { get; }

    public IMongoCollection<BsonDocument> DataProtectionKeys { get; }

    public Task PingAsync(CancellationToken cancellationToken = default) =>
        _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);

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
