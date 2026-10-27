using GeoConquest.Models.Domain;
using GeoConquest.Repositories;
using GeoConquest.Services.Npc;

namespace GeoConquest.Services;

/// <summary>
/// All game rules live here: setup, reinforcements, dice combat, conquest, elimination,
/// victory and NPC turns. Controllers stay thin; the GameSession aggregate guards invariants.
/// NPC turns are played in full on the server and recorded in the event log, so the browser
/// can replay them at 1×, 2× or 4× (fast-forward) without skipping anything.
/// </summary>
public sealed class GameEngineService : IGameEngineService
{
    public const int DefaultNpcCount = GameSession.BoardSize - 1;
    public const int MinimumNpcCount = 1;
    public const int ReinforcementsPerCountry = 2;
    private const int MaxNpcTurnsPerRequest = 100;

    private static readonly string[] Palette =
    {
        "#3DDC84", // human: green
        "#FF5C5C", "#4D9BFF", "#B07CFF", "#FFB020", "#22D3EE",
        "#F472B6", "#A3E635", "#FB923C", "#94A3B8"
    };

    private static readonly NpcProfile[] ProfileRotation =
        { NpcProfile.Aggressor, NpcProfile.Defender, NpcProfile.Opportunist };

    private readonly IGameRepository _repository;
    private readonly ICountryCatalog _catalog;
    private readonly IRandomSource _random;
    private readonly IBattleOddsCalculator _odds;
    private readonly INpcStrategy _npc;
    private readonly ILogger<GameEngineService> _logger;

    public GameEngineService(
        IGameRepository repository,
        ICountryCatalog catalog,
        IRandomSource random,
        IBattleOddsCalculator odds,
        INpcStrategy npc,
        ILogger<GameEngineService> logger)
    {
        _repository = repository;
        _catalog = catalog;
        _random = random;
        _odds = odds;
        _npc = npc;
        _logger = logger;
    }

    private enum RollResult
    {
        Continue,
        Conquered,
        Failed
    }

    // ═════════════════════════════ Public API ═════════════════════════════

    public IReadOnlyList<CountryInfo> GetSelectableCountries() => _catalog.GetAll();

    public async Task<GameSession> CreateGameAsync(string userId, string displayName, string countryCode,
        int npcCount = DefaultNpcCount, CancellationToken cancellationToken = default)
    {
        if (npcCount < MinimumNpcCount || npcCount > DefaultNpcCount)
            throw new DomainException($"Choose between {MinimumNpcCount} and {DefaultNpcCount} NPC opponents.");

        var chosen = _catalog.Find(countryCode)
                     ?? throw new DomainException("Please choose a country from the list.");

        // Draw the 9 other countries at random.
        var drawn = _random
            .Shuffle(_catalog.GetAll().Where(c => c.Code != chosen.Code))
            .Take(GameSession.BoardSize - 1)
            .ToList();

        var human = Player.CreateHuman(userId, displayName, Palette[0]);

        var npcs = Enumerable.Range(0, npcCount)
            .Select(i =>
            {
                var profile = ProfileRotation[i % ProfileRotation.Length];
                string name = npcCount == DefaultNpcCount
                    ? $"{drawn[i].Name} · {NpcPersonality.For(profile).Title.Replace("The ", string.Empty)}"
                    : $"NPC {i + 1} · {NpcPersonality.For(profile).Title.Replace("The ", string.Empty)}";
                return Player.CreateNpc(name, profile, Palette[i + 1]);
            })
            .ToList();

        // Human gets their pick; the drawn countries are dealt round-robin to the NPCs.
        var countries = new List<Country> { ToCountry(chosen, human.PlayerId) };
        for (int i = 0; i < drawn.Count; i++)
            countries.Add(ToCountry(drawn[i], npcs[i % npcs.Count].PlayerId));

        var players = new List<Player> { human };
        players.AddRange(npcs);

        var session = GameSession.Create(userId, players, countries);

        // Random turn order decides who goes first.
        var turnOrder = _random.Shuffle(players.Select(p => p.PlayerId)).ToList();
        session.Start(turnOrder);
        session.AppendEvent(GameEvent.Create(GameEventType.GameStarted,
            $"Game started: {human.DisplayName} commands {chosen.Name}. {session.GetCurrentPlayer().DisplayName} moves first."));

        BeginTurn(session);
        RunNpcTurnsUntilHuman(session);

        await _repository.InsertAsync(session, cancellationToken);
        _logger.LogInformation("Game {GameId} created by {UserId} with {NpcCount} NPCs", session.Id, userId, npcCount);
        return session;
    }

    public async Task<GameSession> GetGameAsync(string gameId, CancellationToken cancellationToken = default) =>
        await _repository.GetByIdAsync(gameId, cancellationToken) ?? throw new GameNotFoundException(gameId);

    public Task<GameSession> PlaceReinforcementsAsync(string gameId, string userId, string countryCode, int amount,
        CancellationToken cancellationToken = default) =>
        ExecuteHumanActionAsync(gameId, userId, (session, human) =>
        {
            var country = session.GetCountry(countryCode);
            session.PlaceReinforcements(human.PlayerId, country.Code, amount);
            session.AppendEvent(GameEvent.Create(GameEventType.Reinforced,
                $"{human.DisplayName} places {amount} soldier(s) on {country.Name} ({country.SoldierCount}).",
                human.PlayerId, toCountryCode: country.Code));
        }, cancellationToken);

    public Task<GameSession> TransferAsync(string gameId, string userId, string fromCode, string toCode, int amount,
        CancellationToken cancellationToken = default) =>
        ExecuteHumanActionAsync(gameId, userId,
            (session, human) => ApplyTransfer(session, human, fromCode, toCode, amount, reason: null),
            cancellationToken);

    public Task<GameSession> StartAttackAsync(string gameId, string userId, string fromCode, string toCode,
        CancellationToken cancellationToken = default) =>
        ExecuteHumanActionAsync(gameId, userId,
            (session, human) => ApplyBeginAttack(session, human, fromCode, toCode, reason: null),
            cancellationToken);

    public Task<GameSession> RollAsync(string gameId, string userId, CancellationToken cancellationToken = default) =>
        ExecuteHumanActionAsync(gameId, userId, (session, human) =>
        {
            if (session.Phase != TurnPhase.Attacking)
                throw new DomainException("Start an attack before rolling.");

            ExecuteRoll(session, human);
        }, cancellationToken);

    public Task<GameSession> RetreatAsync(string gameId, string userId, CancellationToken cancellationToken = default) =>
        ExecuteHumanActionAsync(gameId, userId, (session, human) =>
        {
            ApplyRetreat(session, human, reason: null);
            EndTurnAndPlayNpcs(session);
        }, cancellationToken);

    public Task<GameSession> EndTurnAsync(string gameId, string userId, CancellationToken cancellationToken = default) =>
        ExecuteHumanActionAsync(gameId, userId, (session, _) =>
        {
            if (session.Phase == TurnPhase.Attacking)
                throw new DomainException("Roll again or retreat before ending your turn.");
            if (session.Phase == TurnPhase.Reinforcement)
                throw new DomainException($"Place your {session.PendingReinforcements} reinforcement(s) before ending your turn.");

            EndTurnAndPlayNpcs(session);
        }, cancellationToken);

    public double CalculateWinOdds(GameSession session, string fromCode, string toCode)
    {
        ArgumentNullException.ThrowIfNull(session);
        var from = session.GetCountry(fromCode);
        var to = session.GetCountry(toCode);
        return _odds.AttackerWinProbability(from.SoldierCount, to.SoldierCount);
    }

    // ═════════════════════════════ Authorization + persistence ═════════════════════════════

    private async Task<GameSession> ExecuteHumanActionAsync(string gameId, string userId,
        Action<GameSession, Player> action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new UnauthorizedAccessException("Log in to play. Visitors can watch but not move.");

        var session = await GetGameAsync(gameId, cancellationToken);

        if (!session.IsOwnedBy(userId))
            throw new UnauthorizedAccessException("Only the commander who started this game can play it. You can still watch.");
        if (session.Status != GameStatus.InProgress)
            throw new DomainException("This game is over.");
        if (!session.CanUserAct(userId))
            throw new DomainException("It is not your turn yet.");

        action(session, session.GetCurrentPlayer());

        await _repository.UpdateAsync(session, cancellationToken);
        return session;
    }

    // ═════════════════════════════ Turn flow ═════════════════════════════

    /// <summary>Grants reinforcements (2 per country, from round 2) and logs the turn start.</summary>
    private void BeginTurn(GameSession session)
    {
        var player = session.GetCurrentPlayer();
        int owned = session.GetCountriesOwnedBy(player.PlayerId).Count;
        int reinforcements = session.Round > 1 ? owned * ReinforcementsPerCountry : 0;

        session.GrantReinforcements(reinforcements);

        string extra = reinforcements > 0
            ? $" +{reinforcements} reinforcements for {owned} countr{(owned == 1 ? "y" : "ies")}."
            : session.Round == 1 ? " No reinforcements in round 1." : string.Empty;

        session.AppendEvent(GameEvent.Create(GameEventType.TurnStarted,
            $"Round {session.Round}: {player.DisplayName}'s turn.{extra}", player.PlayerId));
    }

    private void EndTurnAndPlayNpcs(GameSession session)
    {
        if (session.Status != GameStatus.InProgress)
            return;

        session.AdvanceTurn();
        BeginTurn(session);
        RunNpcTurnsUntilHuman(session);
    }

    private void RunNpcTurnsUntilHuman(GameSession session)
    {
        int turnsPlayed = 0;

        while (session.Status == GameStatus.InProgress && !session.GetCurrentPlayer().IsHuman())
        {
            if (++turnsPlayed > MaxNpcTurnsPerRequest)
            {
                _logger.LogWarning("Game {GameId}: NPC turn limit reached in one request", session.Id);
                break;
            }

            PlayNpcTurn(session, session.GetCurrentPlayer());

            if (session.Status != GameStatus.InProgress)
                break;

            session.AdvanceTurn();
            BeginTurn(session);
        }
    }

    // ═════════════════════════════ NPC turn ═════════════════════════════

    private void PlayNpcTurn(GameSession session, Player npc)
    {
        // 1. Reinforce
        foreach (var order in _npc.PlanReinforcements(session, npc))
        {
            session.PlaceReinforcements(npc.PlayerId, order.CountryCode, order.Amount);
            var country = session.GetCountry(order.CountryCode);
            session.AppendEvent(GameEvent.Create(GameEventType.Reinforced,
                $"{npc.DisplayName} places {order.Amount} on {country.Name} ({country.SoldierCount}).",
                npc.PlayerId, order.Reason, toCountryCode: country.Code));
        }

        // 2. Attack (at most one)
        var plan = _npc.ChooseAttack(session, npc);
        if (!plan.ShouldAttack)
        {
            session.AppendEvent(GameEvent.Create(GameEventType.AttackSkipped,
                $"{npc.DisplayName} does not attack this turn.", npc.PlayerId, plan.Reason));
        }
        else
        {
            var attacker = session.GetCountry(plan.FromCode!);
            ApplyBeginAttack(session, npc, plan.FromCode!, plan.ToCode!, plan.Reason);

            var result = RollResult.Continue;
            while (result == RollResult.Continue)
            {
                result = ExecuteRoll(session, npc);
                if (result != RollResult.Continue)
                    break;

                var verdict = _npc.ShouldContinueAttack(session, npc);
                if (!verdict.KeepAttacking)
                {
                    ApplyRetreat(session, npc, verdict.Reason);
                    return; // stopping an attack ends the turn
                }
            }

            if (session.Status != GameStatus.InProgress)
                return;

            if (result == RollResult.Conquered)
            {
                var conquered = session.GetCountry(plan.ToCode!);
                var move = _npc.PlanPostConquestMove(session, npc, attacker, conquered);
                if (move is not null)
                    ApplyTransfer(session, npc, move.FromCode, move.ToCode, move.Amount, move.Reason);
            }
        }

        // 3. Fortify
        var fortify = _npc.PlanFortify(session, npc);
        if (fortify is not null)
            ApplyTransfer(session, npc, fortify.FromCode, fortify.ToCode, fortify.Amount, fortify.Reason);
    }

    // ═════════════════════════════ Shared actions (human + NPC) ═════════════════════════════

    private void ApplyTransfer(GameSession session, Player player, string fromCode, string toCode, int amount, string? reason)
    {
        session.TransferSoldiers(player.PlayerId, fromCode, toCode, amount);
        var from = session.GetCountry(fromCode);
        var to = session.GetCountry(toCode);

        session.AppendEvent(GameEvent.Create(GameEventType.Transferred,
            $"{player.DisplayName} moves {amount} soldier(s) from {from.Name} ({from.SoldierCount}) to {to.Name} ({to.SoldierCount}).",
            player.PlayerId, reason, from.Code, to.Code));
    }

    private void ApplyBeginAttack(GameSession session, Player player, string fromCode, string toCode, string? reason)
    {
        session.BeginAttack(player.PlayerId, fromCode, toCode);
        var attacker = session.GetActiveAttacker();
        var defender = session.GetActiveDefender();
        double odds = _odds.AttackerWinProbability(attacker.SoldierCount, defender.SoldierCount);
        string defenderOwner = session.GetPlayer(defender.OwnerId).DisplayName;

        session.AppendEvent(GameEvent.Create(GameEventType.AttackStarted,
            $"{player.DisplayName} attacks {defender.Name} ({defender.SoldierCount}, {defenderOwner}) from {attacker.Name} ({attacker.SoldierCount}). Win odds {odds:P0}.",
            player.PlayerId, reason, attacker.Code, defender.Code, odds));
    }

    private void ApplyRetreat(GameSession session, Player player, string? reason)
    {
        var attacker = session.GetActiveAttacker();
        var defender = session.GetActiveDefender();
        session.EndAttack();

        session.AppendEvent(GameEvent.Create(GameEventType.AttackRetreated,
            $"{player.DisplayName} calls off the attack on {defender.Name}. {attacker.Name} keeps {attacker.SoldierCount} soldier(s).",
            player.PlayerId, reason, attacker.Code, defender.Code));
    }

    /// <summary>
    /// One roll: n = min(attacker − 1, defender) dice each, paired in order (die 1 vs die 1, …).
    /// Higher die wins the pair; ties change nothing.
    /// </summary>
    private RollResult ExecuteRoll(GameSession session, Player player)
    {
        var attacker = session.GetActiveAttacker();
        var defender = session.GetActiveDefender();

        int dice = Math.Min(attacker.GetAvailableAttackers(), defender.SoldierCount);
        var attackerDice = new int[dice];
        var defenderDice = new int[dice];
        int attackerLosses = 0, defenderLosses = 0;

        for (int i = 0; i < dice; i++)
        {
            attackerDice[i] = _random.RollDie();
            defenderDice[i] = _random.RollDie();

            if (attackerDice[i] > defenderDice[i]) defenderLosses++;
            else if (defenderDice[i] > attackerDice[i]) attackerLosses++;
        }

        if (attackerLosses > 0) attacker.RemoveSoldiers(attackerLosses);
        if (defenderLosses > 0) defender.RemoveSoldiers(defenderLosses);

        double? oddsNow = defender.IsDefeated() || !attacker.CanLaunchAttack()
            ? null
            : _odds.AttackerWinProbability(attacker.SoldierCount, defender.SoldierCount);

        int ties = dice - attackerLosses - defenderLosses;
        session.AppendEvent(GameEvent.DiceRoll(
            player.PlayerId, attacker.Code, defender.Code, attackerDice, defenderDice,
            attacker.SoldierCount, defender.SoldierCount, oddsNow,
            $"{dice} duel(s): {attacker.Name} −{attackerLosses}, {defender.Name} −{defenderLosses}" +
            (ties > 0 ? $", {ties} tie(s)" : string.Empty) +
            $". Now {attacker.Name} {attacker.SoldierCount} vs {defender.Name} {defender.SoldierCount}."));

        if (defender.IsDefeated())
        {
            Conquer(session, player, attacker, defender);
            return RollResult.Conquered;
        }

        if (!attacker.CanLaunchAttack())
        {
            session.EndAttack();
            session.AppendEvent(GameEvent.Create(GameEventType.AttackFailed,
                $"{player.DisplayName}'s attack on {defender.Name} fails. {attacker.Name} holds with 1 soldier.",
                player.PlayerId, fromCountryCode: attacker.Code, toCountryCode: defender.Code));
            return RollResult.Failed;
        }

        return RollResult.Continue;
    }

    /// <summary>Takes the country, pays Max(2, value / 2) spoils, then checks elimination and victory.</summary>
    private void Conquer(GameSession session, Player conqueror, Country attacker, Country defender)
    {
        var loser = session.GetPlayer(defender.OwnerId);

        defender.ChangeOwner(conqueror.PlayerId);
        int reward = defender.GetConquestReward();
        defender.AddSoldiers(reward);
        session.EndAttack();

        session.AppendEvent(GameEvent.Create(GameEventType.CountryConquered,
            $"{conqueror.DisplayName} conquers {defender.Name} from {loser.DisplayName}! +{reward} soldiers stationed there.",
            conqueror.PlayerId, fromCountryCode: attacker.Code, toCountryCode: defender.Code));

        if (session.GetCountriesOwnedBy(loser.PlayerId).Count == 0)
        {
            session.EliminatePlayer(loser.PlayerId);
            session.AppendEvent(GameEvent.Create(GameEventType.PlayerEliminated,
                $"{loser.DisplayName} has lost every country and is eliminated.", loser.PlayerId));

            if (loser.IsHuman())
            {
                session.ConcludeWithHumanDefeat();
                session.AppendEvent(GameEvent.Create(GameEventType.GameLost,
                    $"Game over. {conqueror.DisplayName} has wiped out {loser.DisplayName}.", conqueror.PlayerId));
                return;
            }
        }

        if (session.GetCountriesOwnedBy(conqueror.PlayerId).Count == GameSession.BoardSize)
        {
            session.DeclareWinner(conqueror.PlayerId);
            session.AppendEvent(GameEvent.Create(GameEventType.GameWon,
                $"{conqueror.DisplayName} has conquered all {GameSession.BoardSize} countries and wins the game!",
                conqueror.PlayerId));
        }
    }

    private static Country ToCountry(CountryInfo info, string ownerId) =>
        Country.Create(info.Code, info.Name, info.Population, ownerId);
}
