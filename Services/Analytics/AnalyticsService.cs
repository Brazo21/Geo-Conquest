using GeoConquest.Models.Domain;
using GeoConquest.Models.ViewModels;
using GeoConquest.Repositories;
using GeoConquest.Services.Npc;

namespace GeoConquest.Services.Analytics;

public interface IAnalyticsService
{
    Task<LeaderboardViewModel> GetLeaderboardAsync(string tab, string? userId, CancellationToken cancellationToken = default);

    Task<AnalyticsViewModel> GetAnalyticsAsync(string userId, string commander, string? replayGameId, string tab,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Leaderboard, personal analytics and replays, all computed from the stored games
/// (no separate stats collection to keep in sync). Board snapshots on each event make
/// replays and "territory over time" possible.
/// </summary>
public sealed class AnalyticsService : IAnalyticsService
{
    private const int LeaderboardSize = 50;
    private const int PersonalGameLimit = 40;

    private readonly IGameRepository _games;
    private readonly ICountryCatalog _catalog;

    public AnalyticsService(IGameRepository games, ICountryCatalog catalog)
    {
        _games = games;
        _catalog = catalog;
    }

    // ═════════════════════════════ Leaderboard ═════════════════════════════

    public async Task<LeaderboardViewModel> GetLeaderboardAsync(string tab, string? userId, CancellationToken cancellationToken = default)
    {
        tab = tab is LeaderboardViewModel.TabWeek or LeaderboardViewModel.TabFastest or LeaderboardViewModel.TabVatican
            ? tab
            : LeaderboardViewModel.TabAll;

        DateTime? since = tab == LeaderboardViewModel.TabWeek ? DateTime.UtcNow.AddDays(-7) : null;
        var games = await _games.ListForStatsAsync(since, cancellationToken: cancellationToken);
        var facts = games.Select(Facts).ToList();

        var players = facts
            .GroupBy(f => f.UserId)
            .Select(g =>
            {
                var wins = g.Where(f => f.Won).ToList();
                var counted = tab == LeaderboardViewModel.TabVatican ? wins.Where(f => f.StartValue == 1).ToList() : wins;
                int finished = g.Count(f => f.Finished);
                return new
                {
                    UserId = g.Key,
                    Commander = g.First().Commander,              // games are newest first
                    Games = g.Count(),
                    Finished = finished,
                    Wins = counted.Count,
                    WinRate = finished == 0 ? (double?)null : (double)wins.Count / finished,
                    Fastest = counted.Count == 0 ? (int?)null : counted.Min(f => f.Rounds),
                    Favourite = g.Where(f => f.StartName is not null).GroupBy(f => f.StartName)
                        .OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault()
                };
            })
            .ToList();

        var ordered = tab switch
        {
            LeaderboardViewModel.TabFastest => players.Where(p => p.Fastest is not null)
                .OrderBy(p => p.Fastest).ThenByDescending(p => p.Wins),
            LeaderboardViewModel.TabVatican => players.Where(p => p.Wins > 0)
                .OrderByDescending(p => p.Wins).ThenBy(p => p.Fastest),
            _ => players.OrderByDescending(p => p.Wins).ThenByDescending(p => p.WinRate ?? 0).ThenByDescending(p => p.Games)
        };

        var rows = ordered
            .Take(LeaderboardSize)
            .Select((p, i) => new LeaderRow(i + 1, p.UserId, p.Commander, p.Games, p.Finished, p.Wins,
                p.WinRate, p.Fastest, p.Favourite, p.UserId == userId))
            .ToList();

        MyRankInfo? myRank = null;
        var me = rows.FirstOrDefault(r => r.IsMe);
        if (me is not null)
        {
            string? gap = null;
            if (me.Rank > 1)
            {
                var above = rows[me.Rank - 2];
                int needed = Math.Max(1, above.Wins - me.Wins + (above.Wins == me.Wins ? 0 : 1));
                gap = $"{needed} win{(needed == 1 ? "" : "s")} from rank #{above.Rank}";
            }
            myRank = new MyRankInfo(me.Rank, me.Wins, me.Games, gap);
        }

        return new LeaderboardViewModel
        {
            Tab = tab,
            Rows = rows,
            MyRank = myRank,
            IsLoggedIn = userId is not null,
            WinRateByStart = WinRateByStart(facts),
            HallOfHonour = await HallOfHonourAsync(facts, cancellationToken),
            TotalGames = facts.Count
        };
    }

    private async Task<IReadOnlyList<HonourItem>> HallOfHonourAsync(List<GameFacts> facts, CancellationToken cancellationToken)
    {
        var hall = new List<HonourItem>();
        var wins = facts.Where(f => f.Won).ToList();

        if (wins.Count > 0)
        {
            var fastest = wins.OrderBy(f => f.Rounds).First();
            hall.Add(new HonourItem("Fastest conquest", $"{fastest.Rounds} rounds", fastest.Commander));

            var underdog = wins.OrderBy(f => f.StartValue).ThenBy(f => f.Rounds).First();
            hall.Add(new HonourItem("Underdog victory", $"from {underdog.StartName} ({underdog.StartValue} soldier{(underdog.StartValue == 1 ? "" : "s")})", underdog.Commander));

            // Biggest comeback: the fewest countries held at any point in a game that was still won.
            var recentWins = await _games.ListFullAsync(null, wins.Take(30).Select(w => w.GameId).ToList(), 30, cancellationToken);
            var comeback = recentWins
                .Select(g => (Game: g, Low: LowestCountriesHeld(g)))
                .Where(x => x.Low > 0)
                .OrderBy(x => x.Low)
                .FirstOrDefault();
            if (comeback.Game is not null)
                hall.Add(new HonourItem("Biggest comeback", $"{comeback.Low} → {GameSession.BoardSize} countries", comeback.Game.GetHumanPlayer().DisplayName));
        }

        var busiest = facts.GroupBy(f => f.UserId).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (busiest is not null)
            hall.Add(new HonourItem("Most campaigns", $"{busiest.Count()} games", busiest.First().Commander));

        return hall;
    }

    private static int LowestCountriesHeld(GameSession game)
    {
        int humanIndex = game.IndexOfPlayer(game.GetHumanPlayer().PlayerId);
        return game.Events
            .Select(e => e.ReadBoard())
            .Where(b => b.Count > 0)
            .Select(b => b.Count(x => x.OwnerIndex == humanIndex))
            .DefaultIfEmpty(0)
            .Min();
    }

    private IReadOnlyList<RateBar> WinRateByStart(IEnumerable<GameFacts> facts) =>
        facts.Where(f => f.Finished && f.StartName is not null)
            .GroupBy(f => f.StartName!)
            .Select(g => new RateBar(g.Key, (double)g.Count(f => f.Won) / g.Count(), g.Count()))
            .OrderByDescending(r => r.Rate).ThenByDescending(r => r.SampleSize)
            .Take(8)
            .ToList();

    // ═════════════════════════════ Personal analytics ═════════════════════════════

    public async Task<AnalyticsViewModel> GetAnalyticsAsync(string userId, string commander, string? replayGameId, string tab,
        CancellationToken cancellationToken = default)
    {
        var mine = await _games.ListFullAsync(userId, null, PersonalGameLimit, cancellationToken);
        var everyone = await _games.ListForStatsAsync(cancellationToken: cancellationToken);

        int battles = 0, duelsWon = 0, duelsDecided = 0, bestStreak = 0, conquered = 0;
        (int Net, int Dice, string Text)? luckiest = null;

        foreach (var game in mine)
        {
            string humanId = game.GetHumanPlayer().PlayerId;
            int humanIndex = game.IndexOfPlayer(humanId);
            int streak = 0;

            foreach (var e in game.Events.OrderBy(e => e.Sequence))
            {
                if (e.Type == GameEventType.AttackStarted && e.ActorPlayerId == humanId) battles++;
                if (e.Type == GameEventType.CountryConquered && e.ActorPlayerId == humanId) conquered++;
                if (e.Type != GameEventType.DiceRolled) continue;

                bool attacking = e.ActorPlayerId == humanId;
                bool defending = !attacking && e.ReadBoard().Any(b => b.Code == e.ToCountryCode && b.OwnerIndex == humanIndex);
                if (!attacking && !defending) continue;

                var mineDice = attacking ? e.AttackerDice : e.DefenderDice;
                var theirDice = attacking ? e.DefenderDice : e.AttackerDice;
                int won = 0, lost = 0;

                for (int i = 0; i < mineDice.Count; i++)
                {
                    if (mineDice[i] == theirDice[i]) continue;          // ties decide nothing
                    duelsDecided++;
                    if (mineDice[i] > theirDice[i]) { won++; duelsWon++; streak++; bestStreak = Math.Max(bestStreak, streak); }
                    else { lost++; streak = 0; }
                }

                int net = won - lost;
                if (luckiest is null || net > luckiest.Value.Net || (net == luckiest.Value.Net && mineDice.Count > luckiest.Value.Dice))
                    luckiest = (net, mineDice.Count, $"{string.Join(",", mineDice)} vs {string.Join(",", theirDice)}");
            }
        }

        // Replay: the requested game (any game can be replayed: games are public), else my latest.
        GameSession? replayGame = null;
        if (!string.IsNullOrWhiteSpace(replayGameId))
        {
            replayGame = mine.FirstOrDefault(g => g.Id == replayGameId)
                         ?? (await _games.ListFullAsync(null, new[] { replayGameId }, 1, cancellationToken)).FirstOrDefault();
        }
        replayGame ??= mine.FirstOrDefault();

        return new AnalyticsViewModel
        {
            Tab = tab == AnalyticsViewModel.TabNpc ? AnalyticsViewModel.TabNpc : AnalyticsViewModel.TabMe,
            Commander = commander,
            Games = mine.Count,
            Wins = mine.Count(g => g.DidHumanWin()),
            AverageRounds = mine.Count == 0 ? 0 : Math.Round(mine.Average(g => g.Round), 1),
            Conquered = conquered,
            BattlesFought = battles,
            DuelsWon = duelsWon,
            DuelsDecided = duelsDecided,
            BestStreak = bestStreak,
            LuckiestRoll = luckiest is not null && luckiest.Value.Net > 0 ? luckiest.Value.Text : null,
            GlobalWinRateByStart = WinRateByStart(everyone.Select(Facts)),
            ReplayChoices = mine.Select(g => new ReplayChoice(g.Id,
                $"#{g.Id[..4].ToUpperInvariant()} · {StartName(g) ?? "?"} · R{g.Round} · {Outcome(g)}")).ToList(),
            Replay = replayGame is null ? null : BuildReplay(replayGame),
            WarCouncil = WarCouncil(mine)
        };
    }

    private ReplayData BuildReplay(GameSession game)
    {
        var countries = game.Countries.Select(c => new ReplayCountry(c.Code, c.Name)).ToList();
        var codeIndex = countries.Select((c, i) => (c.Code, i)).ToDictionary(x => x.Code, x => x.i);
        var players = game.Players.Select(p => new ReplayPlayer(p.DisplayName, p.ColorHex, p.IsHuman(), Tag(p))).ToList();
        string humanId = game.GetHumanPlayer().PlayerId;
        int humanIndex = game.IndexOfPlayer(humanId);

        var rounds = new List<ReplayRound>();
        var moments = new List<KeyMoment>();
        var steps = new List<ReplayStep>();

        // A frame for every move that changes the board, so even a game still in round 1 plays back.
        (int[] Owners, int[] Soldiers) Snapshot(IReadOnlyList<BoardEntry> board)
        {
            var o = new int[countries.Count];
            var so = new int[countries.Count];
            foreach (var b in board)
            {
                if (!codeIndex.TryGetValue(b.Code, out int i)) continue;
                o[i] = b.OwnerIndex;
                so[i] = b.Soldiers;
            }
            return (o, so);
        }
        IReadOnlyList<BoardEntry> previous = Array.Empty<BoardEntry>();
        bool halfAnnounced = false;

        foreach (var roundGroup in game.Events.OrderBy(e => e.Sequence).GroupBy(e => e.Round))
        {
            foreach (var e in roundGroup)
            {
                var board = e.ReadBoard();
                string countryName = e.ToCountryCode is not null && codeIndex.TryGetValue(e.ToCountryCode, out int ci) ? countries[ci].Name : "?";

                bool isStep = board.Count > 0 && (steps.Count == 0 || e.Type is GameEventType.Reinforced or GameEventType.Transferred
                    or GameEventType.CountryConquered or GameEventType.AttackFailed or GameEventType.AttackRetreated
                    or GameEventType.AllianceFormed or GameEventType.AllianceBroken);
                if (isStep)
                {
                    var (o, so) = Snapshot(board);
                    steps.Add(new ReplayStep(e.Round, steps.Count == 0 ? "The campaign begins" : e.Message, o, so));
                }
                int stepIndex = Math.Max(0, steps.Count - 1);

                if (e.Type == GameEventType.CountryConquered)
                {
                    if (e.ActorPlayerId == humanId)
                        moments.Add(new KeyMoment(e.Round, $"You take {countryName}", true, stepIndex));
                    else if (previous.Any(b => b.Code == e.ToCountryCode && b.OwnerIndex == humanIndex))
                        moments.Add(new KeyMoment(e.Round, $"You lose {countryName} to {game.Players.First(p => p.PlayerId == e.ActorPlayerId).DisplayName}", false, stepIndex));
                }
                else if (e.Type == GameEventType.PlayerEliminated && e.ActorPlayerId != humanId)
                {
                    moments.Add(new KeyMoment(e.Round, e.Message, true, stepIndex));
                }
                else if (e.Type is GameEventType.GameWon or GameEventType.GameLost)
                {
                    moments.Add(new KeyMoment(e.Round, e.Message, e.Type == GameEventType.GameWon && e.ActorPlayerId == humanId, stepIndex));
                }

                if (!halfAnnounced && board.Count(b => b.OwnerIndex == humanIndex) >= GameSession.BoardSize / 2)
                {
                    halfAnnounced = true;
                    moments.Add(new KeyMoment(e.Round, "You hold half the world", true, stepIndex));
                }

                if (board.Count > 0)
                    previous = board;
            }

            if (previous.Count == 0)
                continue;

            var owners = new int[countries.Count];
            var soldiers = new int[countries.Count];
            foreach (var b in previous)
            {
                if (!codeIndex.TryGetValue(b.Code, out int i)) continue;
                owners[i] = b.OwnerIndex;
                soldiers[i] = b.Soldiers;
            }
            rounds.Add(new ReplayRound(roundGroup.Key, owners, soldiers));
        }

        // Very long games: keep the page light by sampling evenly (always keeping the first and last move).
        const int maxSteps = 600;
        if (steps.Count > maxSteps)
        {
            double stride = (steps.Count - 1) / (double)(maxSteps - 1);
            var sampled = Enumerable.Range(0, maxSteps).Select(i => (int)Math.Round(i * stride)).Distinct().ToList();
            var map = sampled.Select((orig, idx) => (orig, idx)).ToList();
            moments = moments.Select(m => m with { Step = map.Last(x => x.orig <= m.Step).idx }).ToList();
            steps = sampled.Select(i => steps[i]).ToList();
        }

        return new ReplayData(game.Id, game.Id[..4].ToUpperInvariant(), players, countries, rounds, moments.Take(40).ToList(), steps);
    }

    private static IReadOnlyList<CouncilEntry> WarCouncil(IEnumerable<GameSession> games) =>
        games
            .SelectMany(g => g.Events
                .Where(e => e.Reasoning is not null && e.ActorPlayerId is not null)
                .Select(e => (Game: g, Event: e)))
            .OrderByDescending(x => x.Event.OccurredAtUtc)
            .Take(10)
            .Select(x =>
            {
                var npc = x.Game.Players.First(p => p.PlayerId == x.Event.ActorPlayerId);
                return new CouncilEntry(Tag(npc), npc.ColorHex, x.Event.Message, x.Event.Reasoning!);
            })
            .ToList();

    // ═════════════════════════════ Helpers ═════════════════════════════

    private sealed record GameFacts(
        string GameId, string UserId, string Commander, bool Finished, bool Won, int Rounds,
        string? StartName, int StartValue);

    private GameFacts Facts(GameSession g)
    {
        var start = g.GetStartingCountryCode() is { } code ? _catalog.Find(code) : null;
        return new GameFacts(
            g.Id, g.OwnerUserId, g.GetHumanPlayer().DisplayName,
            g.Status == GameStatus.Completed, g.DidHumanWin(), g.Round,
            start?.Name, start?.SoldierValue ?? 0);
    }

    private string? StartName(GameSession g) =>
        g.GetStartingCountryCode() is { } code ? _catalog.Find(code)?.Name : null;

    private static string Outcome(GameSession g) => g.Status switch
    {
        GameStatus.Completed => g.DidHumanWin() ? "Victory" : "Defeat",
        GameStatus.Abandoned => "Abandoned",
        _ => "In progress"
    };

    private static string Tag(Player p) => p.IsHuman() ? "YOU" : p.Profile switch
    {
        NpcProfile.Aggressor => "AGG",
        NpcProfile.Defender => "DEF",
        NpcProfile.Opportunist => "OPP",
        _ => "NPC"
    };
}
