using System.Security.Claims;
using GeoConquest.Models.Domain;
using GeoConquest.Models.ViewModels;
using GeoConquest.Security;
using GeoConquest.Services;
using GeoConquest.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GeoConquest.Controllers;

[Authorize(Roles = UserAccount.AdminRole)]
public sealed class AdminController : Controller
{
    private readonly IAdminService _admin;
    private readonly EmailSettings _email;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IAdminService admin, IOptions<EmailSettings> email, IWebHostEnvironment env, ILogger<AdminController> logger)
    {
        _admin = admin;
        _email = email.Value;
        _env = env;
        _logger = logger;
    }

    private string AdminId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, AccountStatus? status, string? role, CancellationToken cancellationToken) =>
        View(await _admin.GetDashboardAsync(AdminId, search, status, role, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Account(string id, CancellationToken cancellationToken)
    {
        if (!IsUserId(id))
            return NotFound();
        var row = await _admin.GetAccountAsync(AdminId, id, cancellationToken);
        return row is null ? NotFound() : View(row);
    }

    [HttpPost]
    public Task<IActionResult> Suspend(string id, SuspendInput input, CancellationToken ct) =>
        RunAsync(id, () => _admin.SuspendAsync(AdminId, id, input.Reason, ct), "Account suspended. They were signed out everywhere.");

    [HttpPost]
    public Task<IActionResult> Reactivate(string id, CancellationToken ct) =>
        RunAsync(id, () => _admin.ReactivateAsync(AdminId, id, ct), "Account reactivated.");

    [HttpPost]
    public Task<IActionResult> MakeAdmin(string id, CancellationToken ct) =>
        RunAsync(id, () => _admin.SetAdminAsync(AdminId, id, true, ct), "Account is now an administrator.");

    [HttpPost]
    public Task<IActionResult> RemoveAdmin(string id, CancellationToken ct) =>
        RunAsync(id, () => _admin.SetAdminAsync(AdminId, id, false, ct), "Administrator rights removed.");

    [HttpPost]
    public Task<IActionResult> SendReset(string id, CancellationToken ct) =>
        RunAsync(id, () => _admin.SendResetLinkAsync(AdminId, id, (uid, token) =>
                PublicLinks.Build(Request, _email, _env, Url.Action("ResetPassword", "Account", new { uid, token })!)
                ?? throw new InvalidOperationException("Email:PublicBaseUrl must be set in production."), ct),
            "A password reset link was emailed to the user.");

    [HttpPost]
    public async Task<IActionResult> Delete(string id, DeleteAccountInput input, CancellationToken ct)
    {
        if (!IsUserId(id))
            return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError();
            return RedirectToAction(nameof(Account), new { id });
        }

        try
        {
            string username = await _admin.DeleteAsync(AdminId, id, input.ConfirmUsername, ct);
            TempData["Success"] = $"Account \"{username}\" and its games were deleted.";
            return RedirectToAction(nameof(Index));
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Account), new { id });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private async Task<IActionResult> RunAsync(string id, Func<Task> action, string success)
    {
        if (!IsUserId(id))
            return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["Error"] = FirstError();
            return RedirectToAction(nameof(Account), new { id });
        }

        try
        {
            await action();
            TempData["Success"] = success;
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Admin action failed on {UserId}", id);
            TempData["Error"] = "That didn't work just now. Please try again.";
        }
        return RedirectToAction(nameof(Account), new { id });
    }

    private string FirstError() => ModelState.Values.SelectMany(v => v.Errors).First().ErrorMessage;

    private static bool IsUserId(string? id) => InputGuard.IsGameId(id);
}
