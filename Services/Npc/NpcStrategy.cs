using GeoConquest.Models.Domain;

namespace GeoConquest.Services.Npc;

/// <summary>
/// The shared NPC brain. Every NPC uses the same algorithm; the personality's weights
/// change what it values. Steps: SCAN every attackable target, SCORE odds × spoils − risk,
/// DECIDE against the personality's thresholds, EXPLAIN in one sentence.
/// </summary>
public sealed class NpcStrategy : INpcStrategy
{
    private const int EliminationBonus = 3;
    private const int BledTargetBonus = 2;
    private const int RevengeBonus = 2;          // NPCs prefer to hit a commander who betrayed them
    private const double LeaderThreatRatio = 1.3; // a leader this much stronger makes NPCs look for allies

    private readonly IBattleOddsCalculator _odds;

    public NpcStrategy(IBattleOddsCalculator odds)
    {
        _odds = odds;
    }

    // ───────────────────────────── Reinforcements ─────────────────────────────

    public IReadOnlyList<ReinforcementOrder> PlanReinforcements(GameSession session, Player npc)
    {
        int pending = session.PendingReinforcements;
        if (pending <= 0)
            return Array.Empty<ReinforcementOrder>();

        var personality = NpcPersonality.For(npc.Profile);
        var owned = session.GetCountriesOwnedBy(npc.PlayerId);

        // Where would a full stack of reinforcements create the best strike?
        var strike = FindBestStrike(session, npc, personality, extraOnSource: pending);
        int attackShare = strike is null ? 0 : (int)Math.Round(pending * personality.ReinforceAttackShare);
        int defendShare = pending - attackShare;

        var orders = new List<ReinforcementOrder>();

        if (attackShare > 0 && strike is not null)
        {
            double before = _odds.AttackerWinProbability(strike.Source.SoldierCount, strike.Target.SoldierCount);
            double after = _odds.AttackerWinProbability(strike.Source.SoldierCount + attackShare, strike.Target.SoldierCount);
            orders.Add(new ReinforcementOrder(
                strike.Source.Code,
                attackShare,
                $"Masses {attackShare} on {strike.Source.Name} to strike {strike.Target.Name}: odds rise from {before:P0} to {after:P0}."));
        }

        if (defendShare > 0)
        {
            var threat = FindMostThreatened(session, npc);
            if (threat is not null && threat.Odds >= 0.2)
            {
                orders.Add(new ReinforcementOrder(
                    threat.Country.Code,
                    defendShare,
                    $"Fortifies {threat.Country.Name}: {threat.Enemy.Name} ({threat.Enemy.SoldierCount}) could take it with {threat.Odds:P0} odds."));
            }
            else
            {
                var strongest = owned.OrderByDescending(c => c.SoldierCount).First();
                orders.Add(new ReinforcementOrder(
                    strongest.Code,
                    defendShare,
                    $"No real threat on the board, so it stacks {defendShare} on {strongest.Name}, its strongest base."));
            }
        }

        // Merge orders that landed on the same country.
        return orders
            .GroupBy(o => o.CountryCode)
            .Select(g => new ReinforcementOrder(g.Key, g.Sum(o => o.Amount), string.Join(" ", g.Select(o => o.Reason))))
            .ToList()
            .AsReadOnly();
    }

    // ───────────────────────────── Attack ─────────────────────────────

    public AttackPlan ChooseAttack(GameSession session, Player npc)
    {
        var personality = NpcPersonality.For(npc.Profile);
        var owned = session.GetCountriesOwnedBy(npc.PlayerId);

        if (owned.All(c => !c.CanLaunchAttack()))
            return new AttackPlan(null, null, 0, "Holds fire: none of its countries has the 2+ soldiers needed to attack.");

        var strike = FindBestStrike(session, npc, personality, extraOnSource: 0);
        if (strike is null)
        {
            var bestOdds = EnumerateOptions(session, npc, extraOnSource: 0)
                .OrderByDescending(o => o.Odds)
                .FirstOrDefault();

            string detail = bestOdds is null
                ? "there is nothing it can reach"
                : $"best was {bestOdds.Target.Name} at {bestOdds.Odds:P0}";

            return new AttackPlan(null, null, bestOdds?.Odds ?? 0,
                $"Holds fire: no target clears its {personality.MinAttackOdds:P0} odds bar ({detail}).");
        }

        string knockout = strike.EliminatesOwner
            ? $", and it would knock {session.GetPlayer(strike.Target.OwnerId).DisplayName} out of the game"
            : string.Empty;
        if (session.HasBetrayed(strike.Target.OwnerId, npc.PlayerId))
            knockout += $", and it settles the score for {session.GetPlayer(strike.Target.OwnerId).DisplayName}'s betrayal";

        return new AttackPlan(
            strike.Source.Code,
            strike.Target.Code,
            strike.Odds,
            $"Best target: {strike.Target.Name} ({strike.Target.SoldierCount}) from {strike.Source.Name} " +
            $"({strike.Source.SoldierCount}), {strike.Odds:P0} odds, spoils +{strike.Target.GetConquestReward()}{knockout}.");
    }

    public ContinueDecision ShouldContinueAttack(GameSession session, Player npc)
    {
        var personality = NpcPersonality.For(npc.Profile);
        var attacker = session.GetActiveAttacker();
        var defender = session.GetActiveDefender();
        double odds = _odds.AttackerWinProbability(attacker.SoldierCount, defender.SoldierCount);

        return odds < personality.RetreatAt
            ? new ContinueDecision(false, odds,
                $"Retreats from {defender.Name}: odds fell to {odds:P0}, under its {personality.RetreatAt:P0} retreat line.")
            : new ContinueDecision(true, odds,
                $"Presses on against {defender.Name} at {odds:P0} odds.");
    }

    // ───────────────────────────── Transfers ─────────────────────────────

    public TransferOrder? PlanPostConquestMove(GameSession session, Player npc, Country from, Country conquered)
    {
        var personality = NpcPersonality.For(npc.Profile);
        int amount = (int)((from.SoldierCount - Country.MinimumGarrison) * personality.PostConquestMoveShare);
        if (amount < 1)
            return null;

        return new TransferOrder(from.Code, conquered.Code, amount,
            $"Moves {amount} from {from.Name} into newly taken {conquered.Name} to hold the ground.");
    }

    public TransferOrder? PlanFortify(GameSession session, Player npc)
    {
        var personality = NpcPersonality.For(npc.Profile);
        var owned = session.GetCountriesOwnedBy(npc.PlayerId);
        if (owned.Count < 2)
            return null;

        var threats = owned
            .Select(c => (Country: c, Threat: ThreatTo(session, c)))
            .ToList();

        var (target, targetThreat) = threats.OrderByDescending(t => t.Threat?.Odds ?? 0).First();
        double danger = targetThreat?.Odds ?? 0;
        if (danger < personality.FortifyThreatLine || targetThreat is null)
            return null;

        var donor = threats
            .Where(t => t.Country.Code != target.Code && t.Country.SoldierCount > Country.MinimumGarrison)
            .Where(t => (t.Threat?.Odds ?? 0) < danger - 0.2)
            .OrderByDescending(t => t.Country.SoldierCount)
            .Select(t => t.Country)
            .FirstOrDefault();
        if (donor is null)
            return null;

        double share = npc.Profile == NpcProfile.Defender ? 0.5 : 0.34;
        int amount = Math.Max(1, (int)((donor.SoldierCount - Country.MinimumGarrison) * share));

        return new TransferOrder(donor.Code, target.Code, amount,
            $"Moves {amount} from safer {donor.Name} to {target.Name}, which {targetThreat.Enemy.Name} threatens at {danger:P0}.");
    }

    // ───────────────────────────── Alliances ─────────────────────────────

    public AllianceDecision ConsiderAlliance(GameSession session, Player npc, Player proposer)
    {
        if (session.HasBetrayed(proposer.PlayerId, npc.PlayerId))
            return new AllianceDecision(false, $"Refuses: it will never trust {proposer.DisplayName} again after that betrayal.");
        if (session.GetActiveAlliance(npc.PlayerId) is { } existing)
            return new AllianceDecision(false,
                $"Refuses: it is already allied with {session.GetPlayer(existing.PartnerOf(npc.PlayerId)).DisplayName}.");
        if (session.GetActivePlayers().Count <= 2)
            return new AllianceDecision(false, "Refuses: with only two commanders left, there is no one to fight together.");

        int mine = session.GetTotalSoldiers(npc.PlayerId);
        int theirs = session.GetTotalSoldiers(proposer.PlayerId);
        var leader = StrongestActive(session);
        int leaderSoldiers = session.GetTotalSoldiers(leader.PlayerId);

        return npc.Profile switch
        {
            NpcProfile.Defender => leader.PlayerId == proposer.PlayerId
                ? new AllianceDecision(false, $"Refuses: {proposer.DisplayName} is already the strongest ({theirs} soldiers). Defenders don't help the leader grow.")
                : new AllianceDecision(true, leader.PlayerId == npc.PlayerId
                    ? "Accepts: a quiet border lets it keep building its garrisons."
                    : $"Accepts: safety in numbers against {leader.DisplayName} ({leaderSoldiers} soldiers)."),

            NpcProfile.Opportunist => leader.PlayerId != npc.PlayerId && leader.PlayerId != proposer.PlayerId &&
                                      leaderSoldiers > Math.Max(mine, theirs)
                ? new AllianceDecision(true, $"Accepts: together they can bleed runaway {leader.DisplayName} ({leaderSoldiers} soldiers).")
                : new AllianceDecision(false, "Refuses: there is no common enemy worth ganging up on right now."),

            NpcProfile.Aggressor => theirs > mine * 1.2
                ? new AllianceDecision(true, $"Accepts, for now: {proposer.DisplayName} ({theirs}) is too strong to fight head-on ({mine}).")
                : new AllianceDecision(false, $"Refuses: it would rather conquer {proposer.DisplayName} ({theirs}) than share the world."),

            _ => new AllianceDecision(false, "Refuses.")
        };
    }

    public AllianceProposal? SeekAlliance(GameSession session, Player npc)
    {
        // Aggressors go it alone; nobody looks for a second pact or allies in a two-player endgame.
        if (npc.Profile == NpcProfile.Aggressor || session.GetActiveAlliance(npc.PlayerId) is not null ||
            session.GetActivePlayers().Count <= 2)
            return null;

        var leader = StrongestActive(session);
        int leaderSoldiers = session.GetTotalSoldiers(leader.PlayerId);
        if (leader.PlayerId == npc.PlayerId || leaderSoldiers < session.GetTotalSoldiers(npc.PlayerId) * LeaderThreatRatio)
            return null;

        // Only other NPCs, and only ones that would actually agree (no pointless refusals in the log).
        foreach (var candidate in session.GetActivePlayers()
                     .Where(p => !p.IsHuman() && p.PlayerId != npc.PlayerId && p.PlayerId != leader.PlayerId)
                     .Where(p => !session.HasBetrayed(p.PlayerId, npc.PlayerId))
                     .OrderByDescending(p => session.GetTotalSoldiers(p.PlayerId)))
        {
            var answer = ConsiderAlliance(session, candidate, npc);
            if (answer.Accept)
                return new AllianceProposal(candidate.PlayerId,
                    $"Seeks help against {leader.DisplayName} ({leaderSoldiers} soldiers).", answer.Reason);
        }

        return null;
    }

    private static Player StrongestActive(GameSession session) =>
        session.GetActivePlayers().OrderByDescending(p => session.GetTotalSoldiers(p.PlayerId)).First();

    // ───────────────────────────── Scoring ─────────────────────────────

    private sealed record StrikeOption(Country Source, Country Target, double Odds, double Score, bool EliminatesOwner, bool Revenge = false);

    private sealed record Threat(Country Country, Country Enemy, double Odds);

    private IEnumerable<StrikeOption> EnumerateOptions(GameSession session, Player npc, int extraOnSource)
    {
        var owned = session.GetCountriesOwnedBy(npc.PlayerId);
        // Allies' countries are off limits while the pact holds.
        var enemies = session.Countries
            .Where(c => !c.IsOwnedBy(npc.PlayerId) && !session.AreAllied(npc.PlayerId, c.OwnerId))
            .ToList();

        foreach (var source in owned)
        {
            int strength = source.SoldierCount + extraOnSource;
            if (strength < Country.MinimumAttackingStrength)
                continue;

            foreach (var target in enemies)
            {
                double odds = _odds.AttackerWinProbability(strength, target.SoldierCount);
                bool eliminates = session.GetCountriesOwnedBy(target.OwnerId).Count == 1;
                bool revenge = session.HasBetrayed(target.OwnerId, npc.PlayerId);
                yield return new StrikeOption(source, target, odds, 0, eliminates, revenge);
            }
        }
    }

    private StrikeOption? FindBestStrike(GameSession session, Player npc, NpcPersonality personality, int extraOnSource)
    {
        StrikeOption? best = null;

        foreach (var option in EnumerateOptions(session, npc, extraOnSource))
        {
            if (option.Odds < personality.MinAttackOdds)
                continue;

            double score = Score(option, personality, option.Source.SoldierCount + extraOnSource);
            if (score <= 0)
                continue;

            if (best is null || score > best.Score)
                best = option with { Score = score };
        }

        return best;
    }

    private static double Score(StrikeOption option, NpcPersonality personality, int attackingStrength)
    {
        double gain = option.Target.GetConquestReward();
        if (option.EliminatesOwner)
            gain += EliminationBonus;
        if (option.Revenge)
            gain += RevengeBonus;
        if (personality.Profile == NpcProfile.Opportunist && option.Target.SoldierCount < option.Target.SoldierValue)
            gain += BledTargetBonus; // the target has already been bled by someone else

        // Aggressors like soft targets: the fewer defenders, the more appealing.
        gain += personality.Aggression / 100.0 * (3.0 / option.Target.SoldierCount);

        double soldiersAtRisk = Math.Min(attackingStrength - 1, option.Target.SoldierCount + 2);
        double reward = option.Odds * gain * (0.5 + personality.Greed / 100.0);
        double risk = (1 - option.Odds) * soldiersAtRisk * (personality.Caution / 100.0);

        return reward - risk;
    }

    private Threat? FindMostThreatened(GameSession session, Player npc) =>
        session.GetCountriesOwnedBy(npc.PlayerId)
            .Select(c => ThreatTo(session, c))
            .Where(t => t is not null)
            .OrderByDescending(t => t!.Odds)
            .FirstOrDefault();

    /// <summary>The enemy country most likely to conquer <paramref name="country"/>, and its odds.</summary>
    private Threat? ThreatTo(GameSession session, Country country)
    {
        Threat? worst = null;

        foreach (var enemy in session.Countries)
        {
            if (enemy.IsOwnedBy(country.OwnerId) || !enemy.CanLaunchAttack() || session.AreAllied(enemy.OwnerId, country.OwnerId))
                continue;

            double odds = _odds.AttackerWinProbability(enemy.SoldierCount, country.SoldierCount);
            if (worst is null || odds > worst.Odds)
                worst = new Threat(country, enemy, odds);
        }

        return worst;
    }
}
