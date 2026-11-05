using GeoConquest.Services.Npc;

namespace GeoConquest.Models.ViewModels;

// ─────────────────────────────── Leaderboard ───────────────────────────────

public sealed class LeaderboardViewModel
{
    public const string TabAll = "all";
    public const string TabWeek = "week";
    public const string TabFastest = "fastest";
    public const string TabVatican = "vatican";

    public string Tab { get; init; } = TabAll;
    public IReadOnlyList<LeaderRow> Rows { get; init; } = Array.Empty<LeaderRow>();
    public MyRankInfo? MyRank { get; init; }
    public bool IsLoggedIn { get; init; }
    public IReadOnlyList<RateBar> WinRateByStart { get; init; } = Array.Empty<RateBar>();
    public IReadOnlyList<HonourItem> HallOfHonour { get; init; } = Array.Empty<HonourItem>();
    public int TotalGames { get; init; }
}

public sealed record LeaderRow(
    int Rank, string UserId, string Commander, int Games, int Finished, int Wins,
    double? WinRate, int? FastestWin, string? Favourite, bool IsMe);

public sealed record MyRankInfo(int Rank, int Wins, int Games, string? Gap);

/// <summary>A labelled percentage bar (e.g. win rate for one starting country).</summary>
public sealed record RateBar(string Label, double Rate, int SampleSize);

public sealed record HonourItem(string Title, string Value, string Commander);

// ─────────────────────────────── Analytics ───────────────────────────────

public sealed class AnalyticsViewModel
{
    public const string TabMe = "me";
    public const string TabNpc = "npc";

    public string Tab { get; init; } = TabMe;
    public string Commander { get; init; } = string.Empty;

    // Tiles
    public int Games { get; init; }
    public int Wins { get; init; }
    public double AverageRounds { get; init; }
    public int Conquered { get; init; }

    // Battle record
    public int BattlesFought { get; init; }
    public int DuelsWon { get; init; }
    public int DuelsDecided { get; init; }
    public int BestStreak { get; init; }
    public string? LuckiestRoll { get; init; }
    public double DuelWinRate => DuelsDecided == 0 ? 0 : (double)DuelsWon / DuelsDecided;

    public IReadOnlyList<RateBar> GlobalWinRateByStart { get; init; } = Array.Empty<RateBar>();

    // Replay
    public IReadOnlyList<ReplayChoice> ReplayChoices { get; init; } = Array.Empty<ReplayChoice>();
    public ReplayData? Replay { get; init; }

    // NPC profiles
    public IReadOnlyList<NpcPersonality> Personalities { get; init; } = NpcPersonality.All;
    public IReadOnlyList<CouncilEntry> WarCouncil { get; init; } = Array.Empty<CouncilEntry>();
}

public sealed record ReplayChoice(string Id, string Label);

/// <summary>Everything the browser needs to replay one game round by round (serialised to JSON).</summary>
public sealed record ReplayData(
    string GameId,
    string ShortId,
    IReadOnlyList<ReplayPlayer> Players,
    IReadOnlyList<ReplayCountry> Countries,
    IReadOnlyList<ReplayRound> Rounds,
    IReadOnlyList<KeyMoment> KeyMoments);

public sealed record ReplayPlayer(string Name, string Color, bool IsHuman, string Tag);

public sealed record ReplayCountry(string Code, string Name);

/// <summary>Board at the end of a round: owner index and soldiers per country (same order as Countries).</summary>
public sealed record ReplayRound(int Round, IReadOnlyList<int> Owners, IReadOnlyList<int> Soldiers);

public sealed record KeyMoment(int Round, string Text, bool Good);

public sealed record CouncilEntry(string Tag, string Color, string Message, string Reason);
