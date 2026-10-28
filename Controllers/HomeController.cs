using System.Diagnostics;
using Geo_Conquest.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geo_Conquest.Controllers;

/// <summary>Welcome page, How to Play, privacy and errors. Open to everyone.</summary>
[AllowAnonymous]
public class HomeController : Controller
{
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

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
