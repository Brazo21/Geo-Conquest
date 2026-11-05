using System.Diagnostics;
using Geo_Conquest.Models;
using GeoConquest.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Geo_Conquest.Controllers;

/// <summary>Welcome page, How to Play, privacy and errors. Open to everyone.</summary>
[AllowAnonymous]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// The first page of the site: log in or sign up. Commanders who are already logged in
    /// go straight to their games.
    /// </summary>
    public IActionResult Index() =>
        User.Identity?.IsAuthenticated == true
            ? RedirectToAction("Index", "Games", new { scope = "mine" })
            : View();

    /// <summary>Rules and controls. New and returning players land here after logging in.</summary>
    public IActionResult HowToPlay() => View();

    [AllowWithoutConsent]
    public IActionResult Privacy() => View();

    /// <summary>
    /// Sound, vibration, display, dice and General preferences. Open to players and spectators;
    /// the choices describe this device, so they are kept in the browser (validated on load).
    /// </summary>
    [AllowWithoutConsent]
    public IActionResult Settings() => View();

    /// <summary>Install GeoConquest on a phone or computer as an app (PWA).</summary>
    [AllowWithoutConsent]
    public IActionResult App() => View();

    /// <summary>Unexpected server errors. The details are logged; the player sees a simple message.</summary>
    [AllowWithoutConsent]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var problem = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (problem?.Error is not null)
            _logger.LogError(problem.Error, "Unhandled error on {Path}", problem.Path);

        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View("Status", ErrorViewModel.ForStatus(500, Activity.Current?.Id ?? HttpContext.TraceIdentifier));
    }

    /// <summary>Friendly pages for 404 (not found), 403 (not allowed), 429 (too many tries) and others.</summary>
    [AllowWithoutConsent]
    [Route("/Home/Status/{code:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code)
    {
        Response.StatusCode = code is >= 400 and <= 599 ? code : 500;
        return View("Status", ErrorViewModel.ForStatus(code, HttpContext.TraceIdentifier));
    }
}
