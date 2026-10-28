using System.Text.RegularExpressions;

namespace GeoConquest.Models.Domain;

/// <summary>
/// A registered commander. Stored in its own "users" collection (accounts outlive games).
/// The password is never stored, only a salted PBKDF2 hash produced by ASP.NET Core's PasswordHasher.
/// </summary>
public sealed class UserAccount
{
    public const string PlayerRole = "Player";

    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9_]{3,20}$", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public string Id { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    /// <summary>Upper-cased copy used for case-insensitive, uniquely indexed lookups.</summary>
    public string NormalizedUsername { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string Role { get; private set; } = PlayerRole;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    // Required by the MongoDB serializer; not usable by application code.
    private UserAccount()
    {
    }

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
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public static string Normalize(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Stores a hash produced by the password hasher (never a plain-text password).</summary>
    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("A password hash is required.");

        PasswordHash = passwordHash;
    }

    public void RecordLogin() => LastLoginAtUtc = DateTime.UtcNow;
}
