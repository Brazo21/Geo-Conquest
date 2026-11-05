using GeoConquest.Models.Domain;

namespace GeoConquest.Repositories;

public sealed record UserQuery(
    string? Search = null,
    AccountStatus? Status = null,
    string? Role = null,
    int Limit = 200);

public sealed record UserCounts(int Total, int Active, int Suspended, int Admins);

public interface IUserRepository
{
    Task<UserAccount?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<UserAccount?> FindByLoginAsync(string usernameOrEmail, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAccount>> ListAsync(UserQuery query, CancellationToken cancellationToken = default);

    Task<UserCounts> CountAsync(CancellationToken cancellationToken = default);

    Task<long> CountActiveAdminsAsync(CancellationToken cancellationToken = default);

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
