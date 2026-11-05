using GeoConquest.Models.Domain;

namespace GeoConquest.Services.Npc;

public sealed record ReinforcementOrder(string CountryCode, int Amount, string Reason);

public sealed record AttackPlan(string? FromCode, string? ToCode, double Odds, string Reason)
{
    public bool ShouldAttack => FromCode is not null && ToCode is not null;
}

public sealed record ContinueDecision(bool KeepAttacking, double Odds, string Reason);

public sealed record TransferOrder(string FromCode, string ToCode, int Amount, string Reason);

public sealed record AllianceDecision(bool Accept, string Reason);

public sealed record AllianceProposal(string PartnerId, string Reason, string PartnerReason);

public interface INpcStrategy
{
    IReadOnlyList<ReinforcementOrder> PlanReinforcements(GameSession session, Player npc);

    AttackPlan ChooseAttack(GameSession session, Player npc);

    ContinueDecision ShouldContinueAttack(GameSession session, Player npc);

    TransferOrder? PlanPostConquestMove(GameSession session, Player npc, Country from, Country conquered);

    TransferOrder? PlanFortify(GameSession session, Player npc);

    AllianceDecision ConsiderAlliance(GameSession session, Player npc, Player proposer);

    AllianceProposal? SeekAlliance(GameSession session, Player npc);
}
