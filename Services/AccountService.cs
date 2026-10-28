using System.Security.Claims;
using GeoConquest.Models.Domain;
using GeoConquest.Repositories;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace GeoConquest.Services;

public interface IAccountService
{
    /// <summary>Creates an account. Throws <see cref="DomainException"/> or <see cref="DuplicateAccountException"/>.</summary>
    Task<UserAccount> RegisterAsync(string username, string email, string displayName, string password,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the account if the login and password match, otherwise null.</summary>
    Task<UserAccount?> ValidateLoginAsync(string usernameOrEmail, string password,
        CancellationToken cancellationToken = default);

    /// <summary>Builds the identity stored in the authentication cookie.</summary>
    ClaimsPrincipal CreatePrincipal(UserAccount account);
}

/// <summary>
/// Registration and login against the MongoDB users collection.
/// Passwords are hashed with ASP.NET Core Identity's PasswordHasher (PBKDF2, salted, iterated).
/// </summary>
public sealed class AccountService : IAccountService
{
    public const string UsernameClaim = "username";
    public const int MinimumPasswordLength = 8;

    private readonly IUserRepository _users;
    private readonly IPasswordHasher<UserAccount> _hasher;
    private readonly ILogger<AccountService> _logger;

    // Verified against when the login doesn't exist, so response time doesn't reveal which usernames exist.
    private readonly Lazy<(UserAccount Account, string Hash)> _decoy;

    public AccountService(IUserRepository users, IPasswordHasher<UserAccount> hasher, ILogger<AccountService> logger)
    {
        _users = users;
        _hasher = hasher;
        _logger = logger;
        _decoy = new Lazy<(UserAccount, string)>(() =>
        {
            var decoy = UserAccount.Register("decoy_user", "decoy@example.com", "Decoy");
            return (decoy, _hasher.HashPassword(decoy, Guid.NewGuid().ToString()));
        });
    }

    public async Task<UserAccount> RegisterAsync(string username, string email, string displayName, string password,
        CancellationToken cancellationToken = default)
    {
        ValidatePasswordStrength(password);

        var account = UserAccount.Register(username, email, displayName);

        if (await _users.ExistsAsync(account.Username, account.Email, cancellationToken))
            throw new DomainException("That username or email is already registered.");

        account.SetPasswordHash(_hasher.HashPassword(account, password));
        await _users.InsertAsync(account, cancellationToken);

        _logger.LogInformation("New account registered: {Username}", account.Username);
        return account;
    }

    public async Task<UserAccount?> ValidateLoginAsync(string usernameOrEmail, string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrEmpty(password))
            return null;

        var account = await _users.FindByLoginAsync(usernameOrEmail, cancellationToken);
        if (account is null)
        {
            var (decoy, hash) = _decoy.Value;
            _hasher.VerifyHashedPassword(decoy, hash, password);
            return null;
        }

        var result = _hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Failed login for {Username}", account.Username);
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            account.SetPasswordHash(_hasher.HashPassword(account, password));

        account.RecordLogin();
        await _users.UpdateAsync(account, cancellationToken);
        return account;
    }

    public ClaimsPrincipal CreatePrincipal(UserAccount account)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id),
            new(ClaimTypes.Name, account.DisplayName),
            new(UsernameClaim, account.Username),
            new(ClaimTypes.Role, account.Role)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    private static void ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength ||
            !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new DomainException(
                $"Passwords need at least {MinimumPasswordLength} characters, including a letter and a number.");
        }
    }
}
