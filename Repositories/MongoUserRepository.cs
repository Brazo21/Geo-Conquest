using GeoConquest.Data;
using GeoConquest.Models.Domain;
using MongoDB.Driver;

namespace GeoConquest.Repositories;

public sealed class MongoUserRepository : IUserRepository
{
    private readonly IMongoCollection<UserAccount> _users;

    public MongoUserRepository(MongoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _users = context.Users;
    }

    public async Task<UserAccount?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return await _users.Find(u => u.Id == id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UserAccount?> FindByLoginAsync(string usernameOrEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail))
            return null;

        string normalized = UserAccount.Normalize(usernameOrEmail);
        return await _users
            .Find(u => u.NormalizedUsername == normalized || u.NormalizedEmail == normalized)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken = default)
    {
        string normalizedUsername = UserAccount.Normalize(username);
        string normalizedEmail = UserAccount.Normalize(email);

        return await _users
            .Find(u => u.NormalizedUsername == normalizedUsername || u.NormalizedEmail == normalizedEmail)
            .AnyAsync(cancellationToken);
    }

    public async Task InsertAsync(UserAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        try
        {
            await _users.InsertOneAsync(account, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The unique indexes are the real guarantee, even if two sign-ups race each other.
            throw new DuplicateAccountException(ex);
        }
    }

    public async Task UpdateAsync(UserAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        await _users.ReplaceOneAsync(u => u.Id == account.Id, account, cancellationToken: cancellationToken);
    }
}
