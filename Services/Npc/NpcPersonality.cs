using GeoConquest.Models.Domain;

namespace GeoConquest.Services.Npc;

/// <summary>
/// Tuning values for each NPC profile (bonus feature: NPC profiles).
/// Aggression, Caution and Greed are 0–100 weights; the odds values are probabilities.
/// </summary>
public sealed record NpcPersonality(
    NpcProfile Profile,
    string Title,
    string Description,
    int Aggression,
    int Caution,
    int Greed,
    double MinAttackOdds,
    double RetreatAt,
    double FortifyThreatLine,
    double ReinforceAttackShare,
    double PostConquestMoveShare)
{
    public static readonly NpcPersonality Aggressor = new(
        NpcProfile.Aggressor, "The Aggressor", "Strikes the weakest target, rarely retreats.",
        Aggression: 90, Caution: 20, Greed: 70,
        MinAttackOdds: 0.45, RetreatAt: 0.25, FortifyThreatLine: 0.75,
        ReinforceAttackShare: 1.0, PostConquestMoveShare: 0.5);

    public static readonly NpcPersonality Defender = new(
        NpcProfile.Defender, "The Defender", "Builds big garrisons, attacks only with strong odds.",
        Aggression: 35, Caution: 85, Greed: 40,
        MinAttackOdds: 0.75, RetreatAt: 0.60, FortifyThreatLine: 0.40,
        ReinforceAttackShare: 0.5, PostConquestMoveShare: 0.0);

    public static readonly NpcPersonality Opportunist = new(
        NpcProfile.Opportunist, "The Opportunist", "Waits for others to bleed, then strikes.",
        Aggression: 55, Caution: 60, Greed: 85,
        MinAttackOdds: 0.60, RetreatAt: 0.40, FortifyThreatLine: 0.60,
        ReinforceAttackShare: 1.0, PostConquestMoveShare: 0.34);

    public static IReadOnlyList<NpcPersonality> All { get; } = new[] { Aggressor, Defender, Opportunist };

    public static NpcPersonality For(NpcProfile profile) => profile switch
    {
        NpcProfile.Aggressor => Aggressor,
        NpcProfile.Defender => Defender,
        NpcProfile.Opportunist => Opportunist,
        _ => throw new DomainException("Human players have no NPC personality.")
    };
}
