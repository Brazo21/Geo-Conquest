using GeoConquest.Models.Domain;

namespace GeoConquest.Repositories;

/// <summary>Filters for the admin account list. All parts are optional and combine with AND.</summary>
public sealed record UserQuery(
    string? Search = null,
    AccountStatus? Status = null,
    string? Role = null,
    int Limit = 200);

/// <summary>Head-counts for the admin dashboard.</summary>
public sealed record UserCounts(int Total, int Active, int Suspended, int Admins);

public interface IUserRepository
{
    Task<UserAccount?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Finds an account by username or email, ignoring case.</summary>
    Task<UserAccount?> FindByLoginAsync(string usernameOrEmail, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken = default);

    /// <summary>Accounts for the admin panel, newest first. Search matches username, commander name or email.</summary>
    Task<IReadOnlyList<UserAccount>> ListAsync(UserQuery query, CancellationToken cancellationToken = default);

    Task<UserCounts> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Active admins (used so the last admin can never be removed or suspended).</summary>
    Task<long> CountActiveAdminsAsync(CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="DuplicateAccountException"/> if the username or email is taken.</summary>
    Task InsertAsync(UserAccount account, CancellationToken cancellationToken = default);

    Task UpdateAsync(UserAccount account, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class DuplicateAccountException : Exception
{
    public DuplicateAccountException(Exception inner)
        : base("That username or email is already registered.", inner)
    {
    }
}
