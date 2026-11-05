using GeoConquest.Models.Domain;
using GeoConquest.Services;

namespace GeoConquest.Models.ViewModels;

// ─────────────────────────────── New game ───────────────────────────────

public sealed class NewGameViewModel
{
    public NewGameInput Input { get; set; } = new();

    public IReadOnlyList<CountryInfo> Countries { get; set; } = Array.Empty<CountryInfo>();
}

// ─────────────────────────────── Game lists ───────────────────────────────

public sealed record GameSummaryViewModel(
    string Id,
    string Commander,
    GameStatus Status,
    int Round,
    int HumanCountries,
    int HumanSoldiers,
    string Outcome,
    DateTime UpdatedAtUtc,
    bool IsMine)
{
    public static GameSummaryViewModel From(GameSession session, string? userId)
    {
        var human = session.GetHumanPlayer();
        string outcome = session.Status switch
        {
            GameStatus.Completed when session.DidHumanWin() => "Victory",
            GameStatus.Completed => "Defeat",
            GameStatus.Abandoned => "Abandoned",
            _ => $"Turn: {session.GetCurrentPlayer().DisplayName}"
        };

        return new GameSummaryViewModel(
            session.Id,
            human.DisplayName,
            session.Status,
            session.Round,
            session.GetCountriesOwnedBy(human.PlayerId).Count,
            session.GetTotalSoldiers(human.PlayerId),
            outcome,
            session.UpdatedAtUtc,
            session.IsOwnedBy(userId));
    }
}

public sealed class GameListViewModel
{
    public const string ScopeMine = "mine";
    public const string ScopeLive = "live";
    public const string ScopeAll = "all";

    public string Scope { get; set; } = ScopeLive;

    public string? Search { get; set; }

    public GameStatus? Status { get; set; }

    public bool IsLoggedIn { get; set; }

    public IReadOnlyList<GameSummaryViewModel> Games { get; set; } = Array.Empty<GameSummaryViewModel>();
}

// ─────────────────────────────── Play / spectate ───────────────────────────────

public sealed record PlayerRow(
    string Id, string Name, string Color, NpcProfile Profile, bool IsHuman,
    bool IsEliminated, bool IsCurrent, int Countries, int Soldiers);

public sealed record CountryRow(
    string Code, string Name, string OwnerId, string OwnerName, string Color,
    int Soldiers, int SoldierValue, bool IsMine, bool IsAllied = false);

/// <summary>One pact on the board, for the Alliances panel.</summary>
public sealed record AllianceRow(
    string FirstName, string FirstColor, string SecondName, string SecondColor,
    int UntilRound, bool InvolvesHuman, string? PartnerOfHumanId);

/// <summary>An NPC the human could offer a pact to.</summary>
public sealed record AllianceCandidate(string PlayerId, string Name, string Color, bool HoldsGrudge);

public sealed record EventRow(
    long Sequence, int Round, GameEventType Type, string Message, string? Reasoning,
    string Color, IReadOnlyList<int> AttackerDice, IReadOnlyList<int> DefenderDice,
    string? Commentary = null, bool CommentaryIsAi = false, string? ActorId = null);

/// <summary>Where a country sits on the world map (ISO numeric id for the shape, lat/lon for the pin).</summary>
public sealed record MapPoint(string Code, string MapId, double Lat, double Lon);

public sealed record BattleView(
    CountryRow Attacker, CountryRow Defender, double Odds,
    IReadOnlyList<int> LastAttackerDice, IReadOnlyList<int> LastDefenderDice);

public sealed class PlayGameViewModel
{
    public string GameId { get; init; } = string.Empty;
    public GameStatus Status { get; init; }
    public int Round { get; init; }
    public TurnPhase Phase { get; init; }
    public string CurrentPlayerName { get; init; } = string.Empty;
    public string CurrentPlayerColor { get; init; } = "#FFFFFF";
    public string CommanderName { get; init; } = string.Empty;

    /// <summary>True only for the logged-in owner, and only on their own turn.</summary>
    public bool CanAct { get; init; }
    public bool IsOwner { get; init; }
    public bool IsSpectator => !IsOwner;

    public int PendingReinforcements { get; init; }
    public bool AttackUsedThisTurn { get; init; }
    public string? OutcomeMessage { get; init; }
    public long LastEventSequence { get; init; }

    /// <summary>When set, the page replays every event after this sequence (the move just made + NPC turns).</summary>
    public long? ReplayFrom { get; set; }

    public bool IsReplaying => ReplayFrom is not null;

    /// <summary>Map positions for the 10 countries on this board.</summary>
    public IReadOnlyList<MapPoint> MapPoints { get; set; } = Array.Empty<MapPoint>();

    public string ShortId => GameId.Length > 4 ? GameId[..4].ToUpperInvariant() : GameId.ToUpperInvariant();

    public PlayerRow Human => Players.First(p => p.IsHuman);

    public IEnumerable<PlayerRow> Npcs => Players.Where(p => !p.IsHuman);

    public int MaxPlayerSoldiers => Math.Max(1, Players.Max(p => p.Soldiers));

    /// <summary>Events of the attack in progress, oldest first (for the engagement terminal's battle log).</summary>
    public IReadOnlyList<EventRow> CurrentBattleEvents =>
        Battle is null
            ? Array.Empty<EventRow>()
            : Events.TakeWhile(e => e.Type != GameEventType.AttackStarted)
                .Concat(Events.SkipWhile(e => e.Type != GameEventType.AttackStarted).Take(1))
                .Reverse()
                .ToList();

    /// <summary>The most recent NPC decisions that carry a "why" (shown in the NPC decisions panel).</summary>
    public IEnumerable<EventRow> RecentDecisions => Events.Where(e => e.Reasoning is not null).Take(6);

    /// <summary>The latest battle roll anywhere in the game (for the "Last roll" box).</summary>
    public EventRow? LastRoll => Events.FirstOrDefault(e => e.Type == GameEventType.DiceRolled);

    /// <summary>Newest piece of battle commentary.</summary>
    public EventRow? LatestCommentary => Events.FirstOrDefault(e => e.Commentary is not null);

    public IReadOnlyList<PlayerRow> Players { get; init; } = Array.Empty<PlayerRow>();
    public IReadOnlyList<CountryRow> Countries { get; init; } = Array.Empty<CountryRow>();
    public IReadOnlyList<EventRow> Events { get; init; } = Array.Empty<EventRow>();
    public BattleView? Battle { get; init; }

    public IEnumerable<CountryRow> MyCountries => Countries.Where(c => c.IsMine);
    /// <summary>Countries the human may attack: not their own, and not an ally's.</summary>
    public IEnumerable<CountryRow> EnemyCountries => Countries.Where(c => !c.IsMine && !c.IsAllied);

    // ── Alliances ──
    public IReadOnlyList<AllianceRow> Alliances { get; init; } = Array.Empty<AllianceRow>();
    public AllianceRow? HumanAlliance => Alliances.FirstOrDefault(a => a.InvolvesHuman);
    public IReadOnlyList<AllianceCandidate> AllianceCandidates { get; init; } = Array.Empty<AllianceCandidate>();
    public bool AllianceProposedThisTurn { get; init; }
    public bool CanProposeAlliance => CanAct && Phase != TurnPhase.Attacking && HumanAlliance is null &&
                                      !AllianceProposedThisTurn && AllianceCandidates.Count > 0;
    public bool CanBreakAlliance => CanAct && Phase != TurnPhase.Attacking && HumanAlliance is not null;

    /// <summary>The general's answer to this turn's proposal (shown right under the Alliance controls).</summary>
    public EventRow? LastAllianceAnswer =>
        Events.FirstOrDefault(e => e.Type is GameEventType.AllianceFormed or GameEventType.AllianceRejected && e.Round == Round);
    public IEnumerable<CountryRow> AttackSources => MyCountries.Where(c => c.Soldiers >= Country.MinimumAttackingStrength);

    public static PlayGameViewModel From(GameSession session, string? userId, IBattleOddsCalculator odds, int recentEvents = 60)
    {
        var human = session.GetHumanPlayer();
        var players = session.Players.ToDictionary(p => p.PlayerId);
        bool inProgress = session.Status == GameStatus.InProgress;
        var current = inProgress ? session.GetCurrentPlayer() : null;

        var countries = session.Countries
            .OrderByDescending(c => c.SoldierCount)
            .Select(c => new CountryRow(
                c.Code, c.Name, c.OwnerId, players[c.OwnerId].DisplayName, players[c.OwnerId].ColorHex,
                c.SoldierCount, c.SoldierValue, c.IsOwnedBy(human.PlayerId),
                session.AreAllied(human.PlayerId, c.OwnerId)))
            .ToList();

        BattleView? battle = null;
        if (inProgress && session.Phase == TurnPhase.Attacking)
        {
            var attacker = countries.Single(c => c.Code == session.ActiveAttackerCode);
            var defender = countries.Single(c => c.Code == session.ActiveDefenderCode);
            var lastRoll = session.Events.LastOrDefault(e => e.Type == GameEventType.DiceRolled);
            bool rollBelongsToThisAttack = lastRoll is not null &&
                                           session.Events.Last(e => e.Type == GameEventType.AttackStarted).Sequence < lastRoll.Sequence;

            battle = new BattleView(
                attacker, defender,
                odds.AttackerWinProbability(attacker.Soldiers, defender.Soldiers),
                rollBelongsToThisAttack ? lastRoll!.AttackerDice : Array.Empty<int>(),
                rollBelongsToThisAttack ? lastRoll!.DefenderDice : Array.Empty<int>());
        }

        string? outcome = session.Status switch
        {
            GameStatus.Completed when session.DidHumanWin() => $"Victory! {human.DisplayName} has conquered the world.",
            GameStatus.Completed => $"Defeat. {human.DisplayName}'s campaign is over.",
            GameStatus.Abandoned => "This game was abandoned.",
            _ => null
        };

        return new PlayGameViewModel
        {
            GameId = session.Id,
            Status = session.Status,
            Round = session.Round,
            Phase = session.Phase,
            CurrentPlayerName = current?.DisplayName ?? "—",
            CurrentPlayerColor = current?.ColorHex ?? "#FFFFFF",
            CommanderName = human.DisplayName,
            CanAct = session.CanUserAct(userId),
            IsOwner = session.IsOwnedBy(userId),
            PendingReinforcements = session.PendingReinforcements,
            AttackUsedThisTurn = session.AttackUsedThisTurn,
            OutcomeMessage = outcome,
            LastEventSequence = session.LastEventSequence,
            Countries = countries,
            Battle = battle,
            AllianceProposedThisTurn = session.AllianceProposedThisTurn,
            Alliances = session.Alliances
                .Where(a => a.IsActive)
                .Select(a => new AllianceRow(
                    players[a.PlayerAId].DisplayName, players[a.PlayerAId].ColorHex,
                    players[a.PlayerBId].DisplayName, players[a.PlayerBId].ColorHex,
                    a.ExpiresAfterRound, a.Includes(human.PlayerId),
                    a.Includes(human.PlayerId) ? a.PartnerOf(human.PlayerId) : null))
                .ToList(),
            AllianceCandidates = session.GetActivePlayers()
                .Where(p => !p.IsHuman() && session.GetActiveAlliance(p.PlayerId) is null)
                .Select(p => new AllianceCandidate(p.PlayerId, p.DisplayName, p.ColorHex, session.HasBetrayed(human.PlayerId, p.PlayerId)))
                .ToList(),
            Players = session.Players
                .OrderBy(p => p.TurnOrder)
                .Select(p => new PlayerRow(
                    p.PlayerId, p.DisplayName, p.ColorHex, p.Profile, p.IsHuman(), p.IsEliminated,
                    current?.PlayerId == p.PlayerId,
                    session.GetCountriesOwnedBy(p.PlayerId).Count,
                    session.GetTotalSoldiers(p.PlayerId)))
                .ToList(),
            Events = session.Events
                .OrderByDescending(e => e.Sequence)
                .Take(recentEvents)
                .Select(e => new EventRow(
                    e.Sequence, e.Round, e.Type, e.Message, e.Reasoning,
                    e.ActorPlayerId is not null && players.TryGetValue(e.ActorPlayerId, out var actor) ? actor.ColorHex : "#AAAAAA",
                    e.AttackerDice, e.DefenderDice, e.Commentary, e.CommentaryIsAi, e.ActorPlayerId))
                .ToList()
        };
    }
}
