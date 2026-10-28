using System.Security.Claims;
using GeoConquest.Models.Domain;
using GeoConquest.Models.ViewModels;
using GeoConquest.Repositories;
using GeoConquest.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeoConquest.Controllers;

/// <summary>
/// Thin controller: reads input, calls the engine, picks a view. No game rules here.
/// Watching (lists, the board) is open to everyone; every move requires a logged-in Player,
/// and the engine additionally checks that the player owns the game and that it is their turn.
/// </summary>
public sealed class GamesController : Controller
{
    private readonly IGameEngineService _engine;
    private readonly IGameRepository _games;
    private readonly IBattleOddsCalculator _odds;

    public GamesController(IGameEngineService engine, IGameRepository games, IBattleOddsCalculator odds)
    {
        _engine = engine;
        _games = games;
        _odds = odds;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ───────────────────────────── Lists (anyone) ─────────────────────────────

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string? scope, string? search, GameStatus? status, CancellationToken cancellationToken)
    {
        bool loggedIn = User.Identity?.IsAuthenticated == true;
        scope = scope?.ToLowerInvariant() switch
        {
            GameListViewModel.ScopeMine when loggedIn => GameListViewModel.ScopeMine,
            GameListViewModel.ScopeAll => GameListViewModel.ScopeAll,
            GameListViewModel.ScopeLive => GameListViewModel.ScopeLive,
            _ => loggedIn ? GameListViewModel.ScopeMine : GameListViewModel.ScopeLive
        };

        var query = new GameQuery(
            OwnerUserId: scope == GameListViewModel.ScopeMine ? CurrentUserId : null,
            Status: scope == GameListViewModel.ScopeLive ? GameStatus.InProgress : status,
            Search: search,
            Limit: 50);

        var sessions = await _games.SearchAsync(query, cancellationToken);

        return View(new GameListViewModel
        {
            Scope = scope,
            Search = search,
            Status = status,
            IsLoggedIn = loggedIn,
            Games = sessions.Select(s => GameSummaryViewModel.From(s, CurrentUserId)).ToList()
        });
    }

    // ───────────────────────────── Watch (anyone) ─────────────────────────────

    /// <summary>The shareable game page, e.g. /game/abc123. Visitors see everything but get no controls.</summary>
    [HttpGet("/game/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Play(string id, long? replay, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _engine.GetGameAsync(id, cancellationToken);
            var model = PlayGameViewModel.From(session, CurrentUserId, _odds);
            model.ReplayFrom = replay is >= 0 && replay < session.LastEventSequence ? replay : null;

            var catalog = _engine.GetSelectableCountries().ToDictionary(c => c.Code);
            model.MapPoints = session.Countries
                .Where(c => catalog.ContainsKey(c.Code))
                .Select(c => catalog[c.Code])
                .Select(c => new MapPoint(c.Code, c.MapId, c.Lat, c.Lon))
                .ToList();

            return View(model);
        }
        catch (GameNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Live feed: events after a sequence number, each with commentary and a board snapshot.</summary>
    [HttpGet("/game/{id}/events")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Events(string id, long after, CancellationToken cancellationToken)
    {
        var session = await _games.GetByIdAsync(id, cancellationToken);
        return session is null ? NotFound() : Json(GameFeedDto.From(session, after, CurrentUserId));
    }

    // ───────────────────────────── Start a game (players) ─────────────────────────────

    [HttpGet]
    [Authorize(Roles = UserAccount.PlayerRole)]
    public IActionResult New() =>
        View(new NewGameViewModel { Countries = _engine.GetSelectableCountries() });

    [HttpPost]
    [Authorize(Roles = UserAccount.PlayerRole)]
    public async Task<IActionResult> New(NewGameViewModel model, CancellationToken cancellationToken)
    {
        model.Countries = _engine.GetSelectableCountries();
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var session = await _engine.CreateGameAsync(
                CurrentUserId!, User.Identity!.Name ?? "Commander", model.Input.CountryCode, model.Input.NpcCount, cancellationToken);
            return RedirectToAction(nameof(Play), new { id = session.Id, replay = 0 });
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    // ───────────────────────────── Moves (owner, on their turn) ─────────────────────────────

    [HttpPost]
    [Authorize(Roles = UserAccount.PlayerRole)]
    public Task<IActionResult> Reinforce(string id, ReinforceInput input, CancellationToken ct) =>
        ExecuteMoveAsync(id, userId => _engine.PlaceReinforcementsAsync(id, userId, input.CountryCode, input.Amount, ct));

    [HttpPost]
    [Authorize(Roles = UserAccount.PlayerRole)]
    public Task<IActionResult> Transfer(string id, TransferInput input, CancellationToken ct) =>
        ExecuteMoveAsync(id, userId => _engine.TransferAsync(id, userId, input.FromCode, input.ToCode, input.Amount, ct));

    [HttpPost]
    [Authorize(Roles = UserAccount.PlayerRole)]
    public Task<IActionResult> Attack(string id, AttackInput input, CancellationToken ct) =>
        ExecuteMoveAsync(id, userId => _engine.StartAttackAsync(id, userId, input.FromCode, input.ToCode, ct));

    [HttpPost]
    [Authorize(Roles = UserAccount.PlayerRole)]
    public Task<IActionResult> Roll(string id, CancellationToken ct) =>
        ExecuteMoveAsync(id, userId => _engine.RollAsync(id, userId, ct));

    [HttpPost]
    [Authorize(Roles = UserAccount.PlayerRole)]
    public Task<IActionResult> Retreat(string id, CancellationToken ct) =>
        ExecuteMoveAsync(id, userId => _engine.RetreatAsync(id, userId, ct));

    [HttpPost]
    [Authorize(Roles = UserAccount.PlayerRole)]
    public Task<IActionResult> EndTurn(string id, CancellationToken ct) =>
        ExecuteMoveAsync(id, userId => _engine.EndTurnAsync(id, userId, ct));

    /// <summary>Runs one move and turns rule violations into a friendly message on the game page.</summary>
    private async Task<IActionResult> ExecuteMoveAsync(string id, Func<string, Task<MoveOutcome>> move)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values.SelectMany(v => v.Errors).First().ErrorMessage;
            return RedirectToAction(nameof(Play), new { id });
        }

        try
        {
            var outcome = await move(CurrentUserId!);
            return RedirectToAction(nameof(Play), new { id, replay = outcome.ReplayFromSequence });
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (ConcurrencyConflictException)
        {
            TempData["Error"] = "The game changed while you were moving (another tab?). The board has been refreshed. Please try again.";
        }
        catch (GameNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return RedirectToAction(nameof(Play), new { id });
    }
}
