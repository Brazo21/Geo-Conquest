using System.ComponentModel.DataAnnotations;
using GeoConquest.Models.Domain;
using GeoConquest.Repositories;

namespace GeoConquest.Models.ViewModels;

public sealed record AdminAccountRow(
    string Id,
    string Username,
    string DisplayName,
    string Email,
    string Role,
    AccountStatus Status,
    int Games,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc,
    DateTime? ConsentGivenAtUtc,
    bool HasCurrentConsent,
    string? SuspensionReason,
    string? SuspendedBy,
    DateTime? SuspendedAtUtc,
    DateTime? PasswordChangedAtUtc,
    bool IsYou)
{
    public bool IsAdmin => Role == UserAccount.AdminRole;

    public bool IsSuspended => Status == AccountStatus.Suspended;

    public static AdminAccountRow From(UserAccount a, int games, string? currentUserId) => new(
        a.Id, a.Username, a.DisplayName, a.Email, a.Role, a.Status, games,
        a.CreatedAtUtc, a.LastLoginAtUtc, a.ConsentGivenAtUtc, a.HasCurrentConsent,
        a.SuspensionReason, a.SuspendedBy, a.SuspendedAtUtc, a.PasswordChangedAtUtc,
        a.Id == currentUserId);
}

public sealed class AdminDashboardViewModel
{
    public string? Search { get; init; }

    public AccountStatus? Status { get; init; }

    public string? Role { get; init; }

    public UserCounts Counts { get; init; } = new(0, 0, 0, 0);

    public IReadOnlyList<AdminAccountRow> Accounts { get; init; } = Array.Empty<AdminAccountRow>();
}

public sealed class SuspendInput
{
    [Required(ErrorMessage = "Give a reason for the suspension.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "The reason must be 3–200 characters.")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class DeleteAccountInput
{
    [Required(ErrorMessage = "Type the username to confirm.")]
    [StringLength(20)]
    public string ConfirmUsername { get; set; } = string.Empty;
}
