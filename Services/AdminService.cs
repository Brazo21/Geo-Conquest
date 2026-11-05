using GeoConquest.Models.Domain;
using GeoConquest.Models.ViewModels;
using GeoConquest.Repositories;
using GeoConquest.Security;

namespace GeoConquest.Services;

public interface IAdminService
{
    Task<AdminDashboardViewModel> GetDashboardAsync(string currentAdminId, string? search, AccountStatus? status, string? role,
        CancellationToken cancellationToken = default);

    Task<AdminAccountRow?> GetAccountAsync(string currentAdminId, string userId, CancellationToken cancellationToken = default);

    Task SuspendAsync(string adminId, string userId, string reason, CancellationToken cancellationToken = default);

    Task ReactivateAsync(string adminId, string userId, CancellationToken cancellationToken = default);

    Task SetAdminAsync(string adminId, string userId, bool makeAdmin, CancellationToken cancellationToken = default);

    Task SendResetLinkAsync(string adminId, string userId, Func<string, string, string> buildLink,
        CancellationToken cancellationToken = default);

    Task<string> DeleteAsync(string adminId, string userId, string confirmUsername, CancellationToken cancellationToken = default);
}

public sealed class AdminService : IAdminService
{
    private readonly IUserRepository _users;
    private readonly IGameRepository _games;
    private readonly IAccountService _accounts;
    private readonly SessionStateCache _sessions;
    private readonly ILogger<AdminService> _logger;

    public AdminService(IUserRepository users, IGameRepository games, IAccountService accounts,
        SessionStateCache sessions, ILogger<AdminService> logger)
    {
        _users = users;
        _games = games;
        _accounts = accounts;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task<AdminDashboardViewModel> GetDashboardAsync(string currentAdminId, string? search, AccountStatus? status,
        string? role, CancellationToken cancellationToken = default)
    {
        search = InputGuard.CleanSearch(search);
        role = role is UserAccount.AdminRole or UserAccount.PlayerRole ? role : null;

        var accounts = await _users.ListAsync(new UserQuery(search, status, role), cancellationToken);
        var games = await _games.CountByOwnersAsync(accounts.Select(a => a.Id).ToList(), cancellationToken);
        var counts = await _users.CountAsync(cancellationToken);

        return new AdminDashboardViewModel
        {
            Search = search,
            Status = status,
            Role = role,
            Counts = counts,
            Accounts = accounts.Select(a => AdminAccountRow.From(a, games.GetValueOrDefault(a.Id), currentAdminId)).ToList()
        };
    }

    public async Task<AdminAccountRow?> GetAccountAsync(string currentAdminId, string userId, CancellationToken cancellationToken = default)
    {
        var account = await _users.GetByIdAsync(userId, cancellationToken);
        if (account is null)
            return null;
        var games = await _games.CountByOwnersAsync(new[] { account.Id }, cancellationToken);
        return AdminAccountRow.From(account, games.GetValueOrDefault(account.Id), currentAdminId);
    }

    public async Task SuspendAsync(string adminId, string userId, string reason, CancellationToken cancellationToken = default)
    {
        var (admin, target) = await LoadAsync(adminId, userId, cancellationToken);
        NotYourself(admin, target, "suspend");
        if (target.IsAdmin)
            await KeepOneAdminAsync(cancellationToken);

        target.Suspend(reason, admin.Username);
        await SaveAsync(target, cancellationToken);
        _logger.LogWarning("AUDIT: {Admin} suspended {User}. Reason: {Reason}", admin.Username, target.Username, target.SuspensionReason);
    }

    public async Task ReactivateAsync(string adminId, string userId, CancellationToken cancellationToken = default)
    {
        var (admin, target) = await LoadAsync(adminId, userId, cancellationToken);
        target.Reactivate();
        await SaveAsync(target, cancellationToken);
        _logger.LogWarning("AUDIT: {Admin} reactivated {User}", admin.Username, target.Username);
    }

    public async Task SetAdminAsync(string adminId, string userId, bool makeAdmin, CancellationToken cancellationToken = default)
    {
        var (admin, target) = await LoadAsync(adminId, userId, cancellationToken);
        NotYourself(admin, target, "change the role of");
        if (makeAdmin && target.IsSuspended)
            throw new DomainException("Reactivate the account before making it an admin.");
        if (!makeAdmin && target.IsAdmin)
            await KeepOneAdminAsync(cancellationToken);

        target.SetRole(makeAdmin ? UserAccount.AdminRole : UserAccount.PlayerRole);
        await SaveAsync(target, cancellationToken);
        _logger.LogWarning("AUDIT: {Admin} set {User} role to {Role}", admin.Username, target.Username, target.Role);
    }

    public async Task SendResetLinkAsync(string adminId, string userId, Func<string, string, string> buildLink,
        CancellationToken cancellationToken = default)
    {
        var (admin, target) = await LoadAsync(adminId, userId, cancellationToken);
        if (target.IsSuspended)
            throw new DomainException("Suspended accounts can't reset their password. Reactivate it first.");

        await _accounts.RequestPasswordResetAsync(target.Username, buildLink, cancellationToken);
        _logger.LogWarning("AUDIT: {Admin} sent a password reset link to {User}", admin.Username, target.Username);
    }

    public async Task<string> DeleteAsync(string adminId, string userId, string confirmUsername, CancellationToken cancellationToken = default)
    {
        var (admin, target) = await LoadAsync(adminId, userId, cancellationToken);
        NotYourself(admin, target, "delete");
        if (!string.Equals((confirmUsername ?? string.Empty).Trim(), target.Username, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"Type \"{target.Username}\" exactly to confirm the deletion.");
        if (target.IsAdmin)
            await KeepOneAdminAsync(cancellationToken);

        long games = await _games.DeleteByOwnerAsync(target.Id, cancellationToken);
        await _users.DeleteAsync(target.Id, cancellationToken);
        _sessions.Invalidate(target.Id);
        _logger.LogWarning("AUDIT: {Admin} deleted account {User} and {Games} games", admin.Username, target.Username, games);
        return target.Username;
    }

    // helpers

    private async Task<(UserAccount Admin, UserAccount Target)> LoadAsync(string adminId, string userId, CancellationToken ct)
    {
        var admin = await _users.GetByIdAsync(adminId, ct);
        // Re-checked against the database, not just the cookie: a demoted admin loses these powers at once.
        if (admin is null || !admin.IsAdmin || admin.IsSuspended)
            throw new UnauthorizedAccessException("Only active administrators can manage accounts.");

        var target = await _users.GetByIdAsync(userId, ct) ?? throw new DomainException("That account no longer exists.");
        return (admin, target);
    }

    private static void NotYourself(UserAccount admin, UserAccount target, string action)
    {
        if (admin.Id == target.Id)
            throw new DomainException($"You can't {action} your own account from the admin panel.");
    }

    private async Task KeepOneAdminAsync(CancellationToken ct)
    {
        if (await _users.CountActiveAdminsAsync(ct) <= 1)
            throw new DomainException("This is the last active administrator. Make someone else an admin first.");
    }

    private async Task SaveAsync(UserAccount account, CancellationToken ct)
    {
        await _users.UpdateAsync(account, ct);
        _sessions.Invalidate(account.Id);        // takes effect on the user's next request
    }
}
