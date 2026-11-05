using System.Security.Claims;
using GeoConquest.Models.Domain;
using GeoConquest.Models.ViewModels;
using GeoConquest.Security;
using GeoConquest.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoConquest.Controllers;

public sealed class StatsController : Controller
{
    private readonly IAnalyticsService _analytics;

    public StatsController(IAnalyticsService analytics)
    {
        _analytics = analytics;
    }

    [HttpGet("/leaderboard")]
    [AllowAnonymous]
    public async Task<IActionResult> Leaderboard(string? tab, CancellationToken cancellationToken) =>
        View(await _analytics.GetLeaderboardAsync(tab ?? LeaderboardViewModel.TabAll,
            User.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken));

    [HttpGet("/analytics")]
    [Authorize(Roles = UserAccount.PlayerRole)]
    public async Task<IActionResult> Analytics(string? tab, string? game, CancellationToken cancellationToken) =>
        View(await _analytics.GetAnalyticsAsync(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            User.Identity?.Name ?? "Commander",
            InputGuard.IsGameId(game) ? game : null,
            tab ?? AnalyticsViewModel.TabMe,
            cancellationToken));
}
