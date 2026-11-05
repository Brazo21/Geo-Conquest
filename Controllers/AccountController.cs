using System.Security.Claims;
using GeoConquest.Models.Domain;
using GeoConquest.Models.ViewModels;
using GeoConquest.Repositories;
using GeoConquest.Security;
using GeoConquest.Services;
using GeoConquest.Services.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace GeoConquest.Controllers;

/// <summary>
/// Sign-up, login, logout, POPIA consent, password reset and "your data". All rules live in <see cref="IAccountService"/>.
/// No class-level [AllowAnonymous]: it would override the [Authorize] on the signed-in-only actions below.
/// Pages without an attribute are open to everyone (the app has no fallback policy).
/// </summary>
public sealed class AccountController : Controller
{
    private const string ServiceDownMessage =
        "We couldn't reach the command server just now. Please try again in a moment.";

    private const string SuspendedMessage =
        "This account is suspended. If you think this is a mistake, contact the site administrator.";

    private readonly IAccountService _accounts;
    private readonly IGameRepository _games;
    private readonly IEmailQueue _emailQueue;
    private readonly EmailSettings _email;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IAccountService accounts, IGameRepository games, IEmailQueue emailQueue,
        IOptions<EmailSettings> email, IWebHostEnvironment env, ILogger<AccountController> logger)
    {
        _accounts = accounts;
        _games = games;
        _emailQueue = emailQueue;
        _email = email.Value;
        _env = env;
        _logger = logger;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ───────────────────────────── Sign up ─────────────────────────────

    [HttpGet]
    public IActionResult Register() =>
        User.Identity?.IsAuthenticated == true ? RedirectToAction("HowToPlay", "Home") : View(new RegisterViewModel());

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var account = await _accounts.RegisterAsync(
                model.Username, model.Email, model.DisplayName, model.Password, model.PopiaConsent, cancellationToken);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, _accounts.CreatePrincipal(account));
            TempData["Success"] = $"Welcome, Commander {account.DisplayName}. Read your briefing, then start your first campaign.";
            return RedirectToAction("HowToPlay", "Home");
        }
        catch (Exception ex) when (ex is DomainException or DuplicateAccountException)
        {
            // Rule problems (weak password, taken username…) are safe to show as-is.
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything else (database down, network…) is logged; the user gets a plain message.
            _logger.LogError(ex, "Registration failed for {Username}", model.Username);
            ModelState.AddModelError(string.Empty, ServiceDownMessage);
            return View(model);
        }
    }

    // ───────────────────────────── Log in / out ─────────────────────────────

    [HttpGet]
    public IActionResult Login(string? returnUrl = null) =>
        User.Identity?.IsAuthenticated == true
            ? RedirectToAction("HowToPlay", "Home")
            : View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        UserAccount? account;
        try
        {
            account = await _accounts.ValidateLoginAsync(model.Login, model.Password, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Login failed unexpectedly");
            ModelState.AddModelError(string.Empty, ServiceDownMessage);
            model.Password = string.Empty;
            return View(model);
        }

        if (account is null)
        {
            // Same message whether the username or the password was wrong.
            ModelState.AddModelError(string.Empty, "Incorrect username/email or password.");
            model.Password = string.Empty;
            return View(model);
        }

        if (account.IsSuspended)
        {
            // Only said after a correct password, so it doesn't reveal anything to guessers.
            ModelState.AddModelError(string.Empty, SuspendedMessage);
            model.Password = string.Empty;
            return View(model);
        }

        var properties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null
        };
        try
        {
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, _accounts.CreatePrincipal(account), properties);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not sign in {Username}", account.Username);
            ModelState.AddModelError(string.Empty, ServiceDownMessage);
            return View(model);
        }

        // Back to a shared game link if that's where they came from (local URLs only: prevents open redirects);
        // otherwise to the briefing. Users without consent are sent to the POPIA page first by the consent filter.
        if (Url.IsLocalUrl(model.ReturnUrl) && model.ReturnUrl != "/")
            return LocalRedirect(model.ReturnUrl!);

        TempData["Success"] = $"Welcome back, Commander {account.DisplayName}.";
        return account.IsAdmin ? RedirectToAction("Index", "Admin") : RedirectToAction("HowToPlay", "Home");
    }

    [HttpPost]
    [Authorize]
    [AllowWithoutConsent]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    [AllowWithoutConsent]
    public IActionResult AccessDenied() => View();

    // ───────────────────────────── POPIA consent ─────────────────────────────

    [HttpGet]
    [Authorize]
    [AllowWithoutConsent]
    public IActionResult Consent(string? returnUrl = null) =>
        User.FindFirstValue(AccountService.ConsentClaim) == UserAccount.CurrentConsentVersion
            ? RedirectToLocal(returnUrl)
            : View(new ConsentViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [Authorize]
    [AllowWithoutConsent]
    public async Task<IActionResult> Consent(ConsentViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var account = await _accounts.RecordConsentAsync(CurrentUserId!, cancellationToken);
            // New cookie carrying the consent version (and current security stamp).
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, _accounts.CreatePrincipal(account));
            TempData["Success"] = "Thank you. Your consent has been recorded.";
            return RedirectToLocal(model.ReturnUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not record consent");
            ModelState.AddModelError(string.Empty, ServiceDownMessage);
            return View(model);
        }
    }

    // ───────────────────────────── Forgot / reset password ─────────────────────────────

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            string? link = await _accounts.RequestPasswordResetAsync(model.Login, ResetLinkBuilder(), cancellationToken);

            // Development only, when no mail server is set up: show the link so the flow can be tested.
            if (link is not null && !_emailQueue.IsConfigured && _env.IsDevelopment())
                TempData["DevResetLink"] = link;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged, but the page looks the same either way (no hints about which accounts exist).
            _logger.LogError(ex, "Password reset request failed");
        }

        return RedirectToAction(nameof(ForgotPasswordSent));
    }

    [HttpGet]
    public IActionResult ForgotPasswordSent() => View();

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string? uid, string? token, CancellationToken cancellationToken)
    {
        var model = new ResetPasswordViewModel { Uid = uid ?? string.Empty, Token = token ?? string.Empty };
        bool valid;
        try
        {
            valid = await _accounts.IsResetLinkValidAsync(model.Uid, model.Token, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not check a reset link");
            valid = false;
        }

        ViewData["LinkValid"] = valid;
        return View(model);
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken cancellationToken)
    {
        ViewData["LinkValid"] = true;
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            await _accounts.ResetPasswordAsync(model.Uid, model.Token, model.Password, cancellationToken);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Success"] = "Your password has been changed. Log in with the new password.";
            return RedirectToAction(nameof(Login));
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewData["LinkValid"] = !ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase);
            return View(model);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Password reset failed");
            ModelState.AddModelError(string.Empty, ServiceDownMessage);
            return View(model);
        }
    }

    // ───────────────────────────── Your data (POPIA access & erasure) ─────────────────────────────

    [HttpGet]
    [Authorize]
    [AllowWithoutConsent]
    public async Task<IActionResult> MyData(CancellationToken cancellationToken)
    {
        var model = await BuildMyDataAsync(cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [Authorize]
    [AllowWithoutConsent]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> DeleteMyAccount([Bind(Prefix = "Delete")] DeleteMyAccountViewModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return await RedisplayMyDataAsync(input, cancellationToken);

        try
        {
            await _accounts.DeleteOwnAccountAsync(CurrentUserId!, input.Password, cancellationToken);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Success"] = "Your account, personal information and games have been permanently deleted.";
            return RedirectToAction("Index", "Home");
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await RedisplayMyDataAsync(input, cancellationToken);
        }
    }

    // ───────────────────────────── Account security (change password / email) ─────────────────────────────

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Security(CancellationToken cancellationToken)
    {
        var model = await BuildSecurityAsync(cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] ChangePasswordViewModel input, CancellationToken cancellationToken)
    {
        ClearOtherForm("EmailChange");
        if (!ModelState.IsValid)
            return await RedisplaySecurityAsync(cancellationToken);

        try
        {
            var account = await _accounts.ChangePasswordAsync(CurrentUserId!, input.CurrentPassword, input.NewPassword, cancellationToken);
            // The security stamp changed, so this browser gets a fresh sign-in cookie; other devices are signed out.
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, _accounts.CreatePrincipal(account));
            TempData["Success"] = "Password changed. Your other devices have been signed out.";
            return RedirectToAction(nameof(Security));
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError("Password", ex.Message);
            return await RedisplaySecurityAsync(cancellationToken);
        }
    }

    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ChangeEmail([Bind(Prefix = "EmailChange")] ChangeEmailViewModel input, CancellationToken cancellationToken)
    {
        ClearOtherForm("Password");
        if (!ModelState.IsValid)
            return await RedisplaySecurityAsync(cancellationToken, input.NewEmail);

        try
        {
            await _accounts.ChangeEmailAsync(CurrentUserId!, input.CurrentPassword, input.NewEmail, cancellationToken);
            TempData["Success"] = "Email changed. Reset links and notices now go to the new address.";
            return RedirectToAction(nameof(Security));
        }
        catch (Exception ex) when (ex is DomainException or DuplicateAccountException)
        {
            ModelState.AddModelError("EmailChange", ex is DuplicateAccountException ? "That email is already used by another account." : ex.Message);
            return await RedisplaySecurityAsync(cancellationToken, input.NewEmail);
        }
    }

    // ───────────────────────────── helpers ─────────────────────────────

    /// <summary>Each form posts alone, so errors from the other (empty) form are dropped.</summary>
    private void ClearOtherForm(string prefix)
    {
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith(prefix + ".", StringComparison.Ordinal)).ToList())
            ModelState.Remove(key);
    }

    private async Task<SecurityViewModel?> BuildSecurityAsync(CancellationToken ct)
    {
        var account = await _accounts.GetAsync(CurrentUserId!, ct);
        return account is null ? null : new SecurityViewModel
        {
            Username = account.Username,
            Email = account.Email,
            PasswordChangedAtUtc = account.PasswordChangedAtUtc
        };
    }

    private async Task<IActionResult> RedisplaySecurityAsync(CancellationToken ct, string? newEmail = null)
    {
        var model = await BuildSecurityAsync(ct);
        if (model is null)
            return NotFound();
        if (newEmail is not null)
            model.EmailChange.NewEmail = newEmail;     // passwords are never sent back to the page
        return View(nameof(Security), model);
    }

    private Func<string, string, string> ResetLinkBuilder() => (uid, token) =>
        PublicLinks.Build(Request, _email, _env, Url.Action(nameof(ResetPassword), "Account", new { uid, token })!)
        ?? throw new InvalidOperationException("Email:PublicBaseUrl must be set in production to send reset links.");

    private IActionResult RedirectToLocal(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) && returnUrl != "/" ? LocalRedirect(returnUrl!) : RedirectToAction("HowToPlay", "Home");

    private async Task<MyDataViewModel?> BuildMyDataAsync(CancellationToken ct)
    {
        var account = await _accounts.GetAsync(CurrentUserId!, ct);
        if (account is null)
            return null;
        var games = await _games.CountByOwnersAsync(new[] { account.Id }, ct);
        return new MyDataViewModel
        {
            Username = account.Username,
            DisplayName = account.DisplayName,
            Email = account.Email,
            Role = account.Role,
            CreatedAtUtc = account.CreatedAtUtc,
            LastLoginAtUtc = account.LastLoginAtUtc,
            ConsentGivenAtUtc = account.ConsentGivenAtUtc,
            ConsentVersion = account.ConsentVersion,
            PasswordChangedAtUtc = account.PasswordChangedAtUtc,
            Games = games.GetValueOrDefault(account.Id)
        };
    }

    private async Task<IActionResult> RedisplayMyDataAsync(DeleteMyAccountViewModel input, CancellationToken ct)
    {
        var model = await BuildMyDataAsync(ct);
        if (model is null)
            return NotFound();
        model.Delete = new DeleteMyAccountViewModel { Confirm = input.Confirm };
        return View(nameof(MyData), model);
    }
}
