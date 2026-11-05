using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GeoConquest.Models.Domain;
using GeoConquest.Repositories;
using GeoConquest.Security;
using GeoConquest.Services.Email;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace GeoConquest.Services;

public interface IAccountService
{
    /// <summary>Creates an account. Throws <see cref="DomainException"/> or <see cref="DuplicateAccountException"/>.</summary>
    Task<UserAccount> RegisterAsync(string username, string email, string displayName, string password,
        bool popiaConsent, CancellationToken cancellationToken = default);

    /// <summary>Returns the account if the login and password match, otherwise null. Suspended accounts are returned too: the caller blocks them.</summary>
    Task<UserAccount?> ValidateLoginAsync(string usernameOrEmail, string password,
        CancellationToken cancellationToken = default);

    /// <summary>Builds the identity stored in the authentication cookie.</summary>
    ClaimsPrincipal CreatePrincipal(UserAccount account);

    Task<UserAccount?> GetAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Records POPIA consent for a user who signed up before the notice existed (or before it changed).</summary>
    Task<UserAccount> RecordConsentAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emails a single-use reset link if the login matches an active account. Does nothing visible otherwise,
    /// so the page never reveals which emails are registered. <paramref name="buildLink"/> gets (userId, token).
    /// Returns the link only so development mode can show it when email is not configured.
    /// </summary>
    Task<string?> RequestPasswordResetAsync(string usernameOrEmail, Func<string, string, string> buildLink,
        CancellationToken cancellationToken = default);

    Task<bool> IsResetLinkValidAsync(string userId, string token, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password from a reset link. Throws <see cref="DomainException"/> if the link is invalid or used.</summary>
    Task ResetPasswordAsync(string userId, string token, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Changes the password after checking the current one. Signs out other devices; returns the updated account.</summary>
    Task<UserAccount> ChangePasswordAsync(string userId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the email after checking the password. The old address is told about the change.</summary>
    Task<UserAccount> ChangeEmailAsync(string userId, string currentPassword, string newEmail,
        CancellationToken cancellationToken = default);

    /// <summary>POPIA right to erasure: deletes the account and every game it owns after checking the password.</summary>
    Task DeleteOwnAccountAsync(string userId, string password, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registration, login, consent and password resets against the MongoDB users collection.
/// Passwords are hashed with ASP.NET Core Identity's PasswordHasher (PBKDF2, salted, iterated).
/// </summary>
public sealed class AccountService : IAccountService
{
    public const string UsernameClaim = "username";
    public const string StampClaim = "stamp";
    public const string ConsentClaim = "popia";
    public const int MinimumPasswordLength = 8;
    public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromMinutes(30);

    private readonly IUserRepository _users;
    private readonly IGameRepository _games;
    private readonly IPasswordHasher<UserAccount> _hasher;
    private readonly IEmailQueue _email;
    private readonly SessionStateCache _sessions;
    private readonly ILogger<AccountService> _logger;

    // Verified against when the login doesn't exist, so response time doesn't reveal which usernames exist.
    private readonly Lazy<(UserAccount Account, string Hash)> _decoy;

    public AccountService(IUserRepository users, IGameRepository games, IPasswordHasher<UserAccount> hasher,
        IEmailQueue email, SessionStateCache sessions, ILogger<AccountService> logger)
    {
        _users = users;
        _games = games;
        _hasher = hasher;
        _email = email;
        _sessions = sessions;
        _logger = logger;
        _decoy = new Lazy<(UserAccount, string)>(() =>
        {
            var decoy = UserAccount.Register("decoy_user", "decoy@example.com", "Decoy");
            return (decoy, _hasher.HashPassword(decoy, Guid.NewGuid().ToString()));
        });
    }

    public async Task<UserAccount> RegisterAsync(string username, string email, string displayName, string password,
        bool popiaConsent, CancellationToken cancellationToken = default)
    {
        // POPIA section 11: personal information may only be processed with consent (checked on the server,
        // not just by the checkbox in the browser).
        if (!popiaConsent)
            throw new DomainException("You need to accept the privacy notice (POPIA consent) to create an account.");

        ValidatePasswordStrength(password);

        var account = UserAccount.Register(username, email, displayName);

        if (await _users.ExistsAsync(account.Username, account.Email, cancellationToken))
            throw new DomainException("That username or email is already registered.");

        account.SetPasswordHash(_hasher.HashPassword(account, password));
        account.RecordConsent();
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

        if (account.IsSuspended)
        {
            _logger.LogWarning("Suspended account {Username} tried to log in", account.Username);
            return account;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            account.SetPasswordHash(_hasher.HashPassword(account, password));

        account.EnsureSecurityStamp();
        account.RecordLogin();
        await _users.UpdateAsync(account, cancellationToken);
        _sessions.Invalidate(account.Id);
        return account;
    }

    public ClaimsPrincipal CreatePrincipal(UserAccount account)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id),
            new(ClaimTypes.Name, account.DisplayName),
            new(UsernameClaim, account.Username),
            new(StampClaim, account.SecurityStamp),
            new(ConsentClaim, account.ConsentVersion ?? string.Empty),
            new(ClaimTypes.Role, UserAccount.PlayerRole)       // every active account can play
        };
        if (account.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, UserAccount.AdminRole));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public Task<UserAccount?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
        _users.GetByIdAsync(userId, cancellationToken);

    public async Task<UserAccount> RecordConsentAsync(string userId, CancellationToken cancellationToken = default)
    {
        var account = await _users.GetByIdAsync(userId, cancellationToken)
                      ?? throw new DomainException("Account not found.");
        account.RecordConsent();
        account.EnsureSecurityStamp();
        await _users.UpdateAsync(account, cancellationToken);
        _sessions.Invalidate(account.Id);
        _logger.LogInformation("POPIA consent {Version} recorded for {Username}", account.ConsentVersion, account.Username);
        return account;
    }

    public async Task<string?> RequestPasswordResetAsync(string usernameOrEmail, Func<string, string, string> buildLink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buildLink);
        if (string.IsNullOrWhiteSpace(usernameOrEmail))
            return null;

        var account = await _users.FindByLoginAsync(usernameOrEmail.Trim(), cancellationToken);
        if (account is null || account.IsSuspended)
        {
            _logger.LogInformation("Password reset requested for an unknown or suspended login");
            return null;
        }

        // 32 random bytes from the OS crypto generator. Only the SHA-256 hash is stored, so a database
        // leak doesn't hand out working reset links.
        string token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        account.BeginPasswordReset(HashToken(token), ResetLinkLifetime);
        await _users.UpdateAsync(account, cancellationToken);

        string link = buildLink(account.Id, token);
        _email.Enqueue(new EmailMessage(
            account.Email,
            "GeoConquest · reset your password",
            $"Hello Commander {account.DisplayName},\n\n" +
            "Someone (hopefully you) asked to reset the password for your GeoConquest account " +
            $"\"{account.Username}\".\n\nOpen this link within {ResetLinkLifetime.TotalMinutes:0} minutes to choose a new password:\n\n" +
            $"{link}\n\nThe link works once. If you didn't ask for this, ignore this email: your password stays the same.\n\n" +
            "GeoConquest Command"));

        _logger.LogInformation("Password reset link issued for {Username}", account.Username);
        return link;
    }

    public async Task<bool> IsResetLinkValidAsync(string userId, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token) || token.Length > 100)
            return false;
        var account = await _users.GetByIdAsync(userId, cancellationToken);
        return account is { IsSuspended: false } && account.IsPasswordResetValid(HashToken(token));
    }

    public async Task ResetPasswordAsync(string userId, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        const string badLink = "This reset link is invalid or has expired. Please ask for a new one.";
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token) || token.Length > 100)
            throw new DomainException(badLink);

        var account = await _users.GetByIdAsync(userId, cancellationToken);
        if (account is null || account.IsSuspended || !account.IsPasswordResetValid(HashToken(token)))
            throw new DomainException(badLink);

        ValidatePasswordStrength(newPassword);
        account.CompletePasswordReset(_hasher.HashPassword(account, newPassword));
        await _users.UpdateAsync(account, cancellationToken);
        _sessions.Invalidate(account.Id);

        _email.Enqueue(new EmailMessage(account.Email, "GeoConquest · your password was changed",
            $"Hello Commander {account.DisplayName},\n\nThe password for \"{account.Username}\" was just changed and every " +
            "device was signed out. If this wasn't you, reset it again straight away and tell the site administrator.\n\nGeoConquest Command"));
        _logger.LogInformation("Password reset completed for {Username}", account.Username);
    }

    public async Task<UserAccount> ChangePasswordAsync(string userId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        var account = await LoadAndVerifyAsync(userId, currentPassword, cancellationToken);
        ValidatePasswordStrength(newPassword);
        if (_hasher.VerifyHashedPassword(account, account.PasswordHash, newPassword) != PasswordVerificationResult.Failed)
            throw new DomainException("Choose a password different from your current one.");

        account.ChangePassword(_hasher.HashPassword(account, newPassword));
        await _users.UpdateAsync(account, cancellationToken);
        _sessions.Invalidate(account.Id);

        _email.Enqueue(new EmailMessage(account.Email, "GeoConquest · your password was changed",
            $"Hello Commander {account.DisplayName},\n\nThe password for \"{account.Username}\" was just changed from the " +
            "Account security page, and your other devices were signed out. If this wasn't you, use \"Forgot password\" " +
            "straight away and tell the site administrator.\n\nGeoConquest Command"));
        _logger.LogInformation("Password changed by {Username}", account.Username);
        return account;
    }

    public async Task<UserAccount> ChangeEmailAsync(string userId, string currentPassword, string newEmail,
        CancellationToken cancellationToken = default)
    {
        var account = await LoadAndVerifyAsync(userId, currentPassword, cancellationToken);
        string oldEmail = account.Email;

        account.ChangeEmail(newEmail);
        var other = await _users.FindByLoginAsync(account.Email, cancellationToken);
        if (other is not null && other.Id != account.Id)
            throw new DomainException("That email is already used by another account.");

        await _users.UpdateAsync(account, cancellationToken);

        // Tell the old address, so a hijacker can't quietly take over the account's recovery email.
        _email.Enqueue(new EmailMessage(oldEmail, "GeoConquest · your email was changed",
            $"Hello Commander {account.DisplayName},\n\nThe email for \"{account.Username}\" was changed from this address " +
            "to a new one. If this wasn't you, contact the site administrator straight away.\n\nGeoConquest Command"));
        _logger.LogInformation("Email changed by {Username}", account.Username);
        return account;
    }

    private async Task<UserAccount> LoadAndVerifyAsync(string userId, string currentPassword, CancellationToken ct)
    {
        var account = await _users.GetByIdAsync(userId, ct) ?? throw new DomainException("Account not found.");
        if (string.IsNullOrEmpty(currentPassword) ||
            _hasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Wrong current password on the security page for {Username}", account.Username);
            throw new DomainException("Your current password is not correct.");
        }
        return account;
    }

    public async Task DeleteOwnAccountAsync(string userId, string password, CancellationToken cancellationToken = default)
    {
        var account = await _users.GetByIdAsync(userId, cancellationToken) ?? throw new DomainException("Account not found.");

        if (string.IsNullOrEmpty(password) ||
            _hasher.VerifyHashedPassword(account, account.PasswordHash, password) == PasswordVerificationResult.Failed)
            throw new DomainException("That password is not correct.");

        if (account.IsAdmin && await _users.CountActiveAdminsAsync(cancellationToken) <= 1)
            throw new DomainException("You are the only administrator. Make someone else an admin before deleting your account.");

        long games = await _games.DeleteByOwnerAsync(account.Id, cancellationToken);
        await _users.DeleteAsync(account.Id, cancellationToken);
        _sessions.Invalidate(account.Id);
        _logger.LogInformation("Account {Username} deleted by its owner ({Games} games removed)", account.Username, games);
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));

    public static void ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength || password.Length > 100 ||
            !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new DomainException(
                $"Passwords need at least {MinimumPasswordLength} characters, including a letter and a number.");
        }
    }
}
