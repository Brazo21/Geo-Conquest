using GeoConquest.Models.Domain;

namespace GeoConquest.Services.Npc;

public sealed record ReinforcementOrder(string CountryCode, int Amount, string Reason);

public sealed record AttackPlan(string? FromCode, string? ToCode, double Odds, string Reason)
{
    public bool ShouldAttack => FromCode is not null && ToCode is not null;
}

public sealed record ContinueDecision(bool KeepAttacking, double Odds, string Reason);

public sealed record TransferOrder(string FromCode, string ToCode, int Amount, string Reason);

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
}
