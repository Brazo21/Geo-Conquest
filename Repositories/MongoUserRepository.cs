using GeoConquest.Data;
using GeoConquest.Models.Domain;
using System.Text.RegularExpressions;
using MongoDB.Bson;
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

    public async Task<IReadOnlyList<UserAccount>> ListAsync(UserQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var f = Builders<UserAccount>.Filter;
        var filter = f.Empty;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Escaped, so the search text is matched literally (no regex injection).
            var pattern = new BsonRegularExpression(Regex.Escape(query.Search.Trim()), "i");
            filter &= f.Regex(u => u.Username, pattern) | f.Regex(u => u.DisplayName, pattern) | f.Regex(u => u.Email, pattern);
        }
        if (query.Status is { } status)
            filter &= f.Eq(u => u.Status, status);
        if (!string.IsNullOrWhiteSpace(query.Role))
            filter &= f.Eq(u => u.Role, query.Role);

        return await _users.Find(filter)
            .SortByDescending(u => u.CreatedAtUtc)
            .Limit(Math.Clamp(query.Limit, 1, 500))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserCounts> CountAsync(CancellationToken cancellationToken = default)
    {
        var f = Builders<UserAccount>.Filter;
        long total = await _users.CountDocumentsAsync(f.Empty, cancellationToken: cancellationToken);
        long suspended = await _users.CountDocumentsAsync(f.Eq(u => u.Status, AccountStatus.Suspended), cancellationToken: cancellationToken);
        long admins = await _users.CountDocumentsAsync(f.Eq(u => u.Role, UserAccount.AdminRole), cancellationToken: cancellationToken);
        return new UserCounts((int)total, (int)(total - suspended), (int)suspended, (int)admins);
    }

    public Task<long> CountActiveAdminsAsync(CancellationToken cancellationToken = default) =>
        _users.CountDocumentsAsync(u => u.Role == UserAccount.AdminRole && u.Status == AccountStatus.Active,
            cancellationToken: cancellationToken);

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        await _users.DeleteOneAsync(u => u.Id == id, cancellationToken);
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
        try
        {
            await _users.ReplaceOneAsync(u => u.Id == account.Id, account, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // e.g. two people switching to the same email at the same moment: the unique index decides.
            throw new DuplicateAccountException(ex);
        }
    }
}
