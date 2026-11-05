using GeoConquest.Models.Domain;
using GeoConquest.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GeoConquest.Services;

public sealed class AdminSeedSettings
{
    public const string SectionName = "Admin";

    public string Username { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string DisplayName { get; init; } = "Site Admin";

    public string Password { get; init; } = string.Empty;
}

public sealed class AdminSeeder
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher<UserAccount> _hasher;
    private readonly AdminSeedSettings _settings;
    private readonly ILogger<AdminSeeder> _logger;

    public AdminSeeder(IUserRepository users, IPasswordHasher<UserAccount> hasher, IOptions<AdminSeedSettings> settings,
        ILogger<AdminSeeder> logger)
    {
        _users = users;
        _hasher = hasher;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Username))
        {
            if (await _users.CountActiveAdminsAsync(cancellationToken) == 0)
                _logger.LogWarning("No administrator configured. Add Admin:Username, Admin:Email and Admin:Password to User Secrets.");
            return;
        }

        var existing = await _users.FindByLoginAsync(_settings.Username, cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsAdmin)
            {
                existing.SetRole(UserAccount.AdminRole);
                await _users.UpdateAsync(existing, cancellationToken);
                _logger.LogInformation("Promoted {Username} to administrator", existing.Username);
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.Password) || string.IsNullOrWhiteSpace(_settings.Email))
        {
            _logger.LogWarning("Admin account {Username} not found and no Admin:Email / Admin:Password to create it.", _settings.Username);
            return;
        }

        AccountService.ValidatePasswordStrength(_settings.Password);
        var admin = UserAccount.Register(_settings.Username, _settings.Email, _settings.DisplayName);
        admin.SetPasswordHash(_hasher.HashPassword(admin, _settings.Password));
        admin.SetRole(UserAccount.AdminRole);
        // Consent is NOT pre-filled: the admin accepts the privacy notice on first login, like everyone else.
        await _users.InsertAsync(admin, cancellationToken);
        _logger.LogInformation("Created administrator account {Username}", admin.Username);
    }
}
