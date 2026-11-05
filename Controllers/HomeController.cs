using System.Diagnostics;
using Geo_Conquest.Models;
using GeoConquest.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Geo_Conquest.Controllers;

[AllowAnonymous]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index() =>
        User.Identity?.IsAuthenticated == true
            ? RedirectToAction("Index", "Games", new { scope = "mine" })
            : View();

    public IActionResult HowToPlay() => View();

    [AllowWithoutConsent]
    public IActionResult Privacy() => View();

    [AllowWithoutConsent]
    public IActionResult Settings() => View();

    [AllowWithoutConsent]
    public IActionResult App() => View();

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

    [AllowWithoutConsent]
    [Route("/Home/Status/{code:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code)
    {
        Response.StatusCode = code is >= 400 and <= 599 ? code : 500;
        return View("Status", ErrorViewModel.ForStatus(code, HttpContext.TraceIdentifier));
    }
}
