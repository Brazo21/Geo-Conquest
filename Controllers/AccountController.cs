using GeoConquest.Models.Domain;
using GeoConquest.Models.ViewModels;
using GeoConquest.Repositories;
using GeoConquest.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeoConquest.Controllers;

/// <summary>Sign-up, login and logout. All logic lives in <see cref="IAccountService"/>.</summary>
[AllowAnonymous]
public sealed class AccountController : Controller
{
    private readonly IAccountService _accounts;

    public AccountController(IAccountService accounts)
    {
        _accounts = accounts;
    }

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
                model.Username, model.Email, model.DisplayName, model.Password, cancellationToken);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, _accounts.CreatePrincipal(account));
            TempData["Success"] = $"Welcome, Commander {account.DisplayName}. Read your briefing, then start your first campaign.";
            return RedirectToAction("HowToPlay", "Home");
        }
        catch (Exception ex) when (ex is DomainException or DuplicateAccountException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

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

        var account = await _accounts.ValidateLoginAsync(model.Login, model.Password, cancellationToken);
        if (account is null)
        {
            // Same message whether the username or the password was wrong.
            ModelState.AddModelError(string.Empty, "Incorrect username/email or password.");
            return View(model);
        }

        var properties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : null
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, _accounts.CreatePrincipal(account), properties);

        // Back to a shared game link if that's where they came from (local URLs only: prevents open redirects);
        // otherwise to the briefing.
        if (Url.IsLocalUrl(model.ReturnUrl) && model.ReturnUrl != "/")
            return LocalRedirect(model.ReturnUrl!);

        TempData["Success"] = $"Welcome back, Commander {account.DisplayName}.";
        return RedirectToAction("HowToPlay", "Home");
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
