using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace GeoConquest.Models.Domain;

public enum AccountStatus
{
    Active,
    Suspended
}

public sealed class UserAccount
{
    public const string PlayerRole = "Player";
    public const string AdminRole = "Admin";

    public const string CurrentConsentVersion = "2026-11";

    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9_]{3,20}$", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public string Id { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public string NormalizedUsername { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string Role { get; private set; } = PlayerRole;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    // Status (admin control)
    public AccountStatus Status { get; private set; } = AccountStatus.Active;

    public DateTime? SuspendedAtUtc { get; private set; }

    public string? SuspensionReason { get; private set; }

    public string? SuspendedBy { get; private set; }

    public string SecurityStamp { get; private set; } = string.Empty;

    // POPIA consent
    public string? ConsentVersion { get; private set; }

    public DateTime? ConsentGivenAtUtc { get; private set; }

    // Password reset (only a hash of the emailed token is stored)
    public string? PasswordResetTokenHash { get; private set; }

    public DateTime? PasswordResetExpiresUtc { get; private set; }

    public DateTime? PasswordChangedAtUtc { get; private set; }

    // Required by the MongoDB serializer; not usable by application code.
    private UserAccount()
    {
    }

    public bool IsAdmin => Role == AdminRole;

    public bool IsSuspended => Status == AccountStatus.Suspended;

    public bool HasCurrentConsent => ConsentVersion == CurrentConsentVersion;

    public static UserAccount Register(string username, string email, string displayName)
    {
        username = (username ?? string.Empty).Trim();
        email = (email ?? string.Empty).Trim();
        displayName = (displayName ?? string.Empty).Trim();

        if (!UsernamePattern.IsMatch(username))
            throw new DomainException("Usernames are 3–20 letters, numbers or underscores.");
        if (email.Length > 254 || !EmailPattern.IsMatch(email))
            throw new DomainException("Please enter a valid email address.");
        if (displayName.Length is < 2 or > 30)
            throw new DomainException("Commander names are 2–30 characters.");

        return new UserAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = username,
            NormalizedUsername = Normalize(username),
            Email = email,
            NormalizedEmail = Normalize(email),
            DisplayName = displayName,
            Role = PlayerRole,
            Status = AccountStatus.Active,
            SecurityStamp = NewStamp(),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static string Normalize(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public bool EnsureSecurityStamp()
    {
        if (!string.IsNullOrEmpty(SecurityStamp))
            return false;
        SecurityStamp = NewStamp();
        return true;
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("A password hash is required.");

        PasswordHash = passwordHash;
    }

    public void RecordLogin() => LastLoginAtUtc = DateTime.UtcNow;

    // POPIA

    public void RecordConsent()
    {
        ConsentVersion = CurrentConsentVersion;
        ConsentGivenAtUtc = DateTime.UtcNow;
    }

    // Password reset

    public void BeginPasswordReset(string tokenHash, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("A reset token is required.");
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(24))
            throw new DomainException("Reset links last between a few minutes and a day.");

        PasswordResetTokenHash = tokenHash;
        PasswordResetExpiresUtc = DateTime.UtcNow.Add(lifetime);
    }

    public bool IsPasswordResetValid(string tokenHash)
    {
        if (string.IsNullOrEmpty(PasswordResetTokenHash) || PasswordResetExpiresUtc is null ||
            PasswordResetExpiresUtc < DateTime.UtcNow || string.IsNullOrEmpty(tokenHash))
            return false;

        var stored = System.Text.Encoding.ASCII.GetBytes(PasswordResetTokenHash);
        var given = System.Text.Encoding.ASCII.GetBytes(tokenHash);
        return stored.Length == given.Length && CryptographicOperations.FixedTimeEquals(stored, given);
    }

    public void CompletePasswordReset(string newPasswordHash)
    {
        SetPasswordHash(newPasswordHash);
        PasswordResetTokenHash = null;
        PasswordResetExpiresUtc = null;
        PasswordChangedAtUtc = DateTime.UtcNow;
        SecurityStamp = NewStamp();
    }

    // Self-service changes (Account security page)

    public void ChangePassword(string newPasswordHash) => CompletePasswordReset(newPasswordHash);

    public void ChangeEmail(string email)
    {
        email = (email ?? string.Empty).Trim();
        if (email.Length > 254 || !EmailPattern.IsMatch(email))
            throw new DomainException("Please enter a valid email address.");
        if (Normalize(email) == NormalizedEmail)
            throw new DomainException("That is already your email address.");

        Email = email;
        NormalizedEmail = Normalize(email);
        PasswordResetTokenHash = null;        // an old reset link must not outlive the address it was sent to
        PasswordResetExpiresUtc = null;
    }

    // Admin control

    public void Suspend(string reason, string byUsername)
    {
        reason = (reason ?? string.Empty).Trim();
        if (reason.Length is < 3 or > 200)
            throw new DomainException("Give a reason for the suspension (3–200 characters).");
        if (IsSuspended)
            throw new DomainException("This account is already suspended.");

        Status = AccountStatus.Suspended;
        SuspensionReason = reason;
        SuspendedBy = byUsername;
        SuspendedAtUtc = DateTime.UtcNow;
        SecurityStamp = NewStamp();          // ends every open session
    }

    public void Reactivate()
    {
        if (!IsSuspended)
            throw new DomainException("This account is not suspended.");

        Status = AccountStatus.Active;
        SuspensionReason = null;
        SuspendedBy = null;
        SuspendedAtUtc = null;
        SecurityStamp = NewStamp();
    }

    public void SetRole(string role)
    {
        if (role is not (PlayerRole or AdminRole))
            throw new DomainException("Unknown role.");
        if (Role == role)
            return;

        Role = role;
        SecurityStamp = NewStamp();          // they log in again with the new rights
    }
}
