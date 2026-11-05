using GeoConquest.Models.Domain;

namespace GeoConquest.Services.Npc;

public sealed record ReinforcementOrder(string CountryCode, int Amount, string Reason);

public sealed record AttackPlan(string? FromCode, string? ToCode, double Odds, string Reason)
{
    public bool ShouldAttack => FromCode is not null && ToCode is not null;
}

public sealed record ContinueDecision(bool KeepAttacking, double Odds, string Reason);

public sealed record TransferOrder(string FromCode, string ToCode, int Amount, string Reason);

/// <summary>An NPC's answer to an alliance proposal, with the reason shown in the game log.</summary>
public sealed record AllianceDecision(bool Accept, string Reason);

/// <summary>An NPC that wants a pact: who it asks, and why.</summary>
public sealed record AllianceProposal(string PartnerId, string Reason, string PartnerReason);

/// <summary>
/// How an NPC thinks: scan every target, score odds × spoils − risk, decide, and explain.
/// Pure decision-making: it reads the board and returns orders with reasons; the engine applies them.
/// </summary>
public interface INpcStrategy
{
    IReadOnlyList<ReinforcementOrder> PlanReinforcements(GameSession session, Player npc);

    AttackPlan ChooseAttack(GameSession session, Player npc);

    ContinueDecision ShouldContinueAttack(GameSession session, Player npc);

    TransferOrder? PlanPostConquestMove(GameSession session, Player npc, Country from, Country conquered);

    TransferOrder? PlanFortify(GameSession session, Player npc);

    /// <summary>Should <paramref name="npc"/> accept a pact offered by <paramref name="proposer"/>? Decided by personality.</summary>
    AllianceDecision ConsiderAlliance(GameSession session, Player npc, Player proposer);

    /// <summary>Whether <paramref name="npc"/> looks for a pact with another NPC this turn (only ones that would say yes).</summary>
    AllianceProposal? SeekAlliance(GameSession session, Player npc);
}
