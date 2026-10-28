using GeoConquest.Models.Domain;

namespace GeoConquest.Repositories;

public interface IUserRepository
{
    Task<UserAccount?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Finds an account by username or email, ignoring case.</summary>
    Task<UserAccount?> FindByLoginAsync(string usernameOrEmail, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="DuplicateAccountException"/> if the username or email is taken.</summary>
    Task InsertAsync(UserAccount account, CancellationToken cancellationToken = default);

    Task UpdateAsync(UserAccount account, CancellationToken cancellationToken = default);
}

public sealed class DuplicateAccountException : Exception
{
    public DuplicateAccountException(Exception inner)
        : base("That username or email is already registered.", inner)
    {
    }
}
