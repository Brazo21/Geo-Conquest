using GeoConquest.Models.Domain;

namespace GeoConquest.Services.Commentary;

/// <summary>
/// Hand-written commentary lines used when the AI is not configured, times out or fails.
/// Varied by event sequence so repeated events don't read identically.
/// </summary>
public static class FallbackCommentary
{
    private static readonly string[] Starts =
    {
        "The drums of war sound! {0} launches an assault on {1}.",
        "Here we go: {0} has its sights locked on {1}.",
        "Bold move! {0} marches on {1}. The world holds its breath."
    };

    private static readonly string[] Rolls =
    {
        "The dice clatter across the war table. Every soldier counts now.",
        "Steel meets steel! Another exchange in this bruising contest.",
        "A tense round of dice. Neither side is giving an inch.",
        "The commanders watch the dice tumble. Fortunes are shifting."
    };

    private static readonly string[] Conquests =
    {
        "It's over! {0} plants its flag. The map has changed.",
        "A stunning conquest! {0} has taken the territory.",
        "Victory for {0}! The defenders could hold no longer."
    };

    private static readonly string[] Failures =
    {
        "The assault crumbles! The defenders stand tall.",
        "Repelled! The attackers are down to their last soldier.",
        "A costly failure. The defence held firm."
    };

    private static readonly string[] Retreats =
    {
        "A tactical withdrawal. Live to fight another day.",
        "The attacker pulls back. Discretion over valour this time."
    };

    public static string For(GameEvent e, string attackerName, string defenderName)
    {
        int i = (int)(e.Sequence % 7);
        return e.Type switch
        {
            GameEventType.AttackStarted => string.Format(Pick(Starts, i), attackerName, defenderName),
            GameEventType.DiceRolled => Pick(Rolls, i),
            GameEventType.CountryConquered => string.Format(Pick(Conquests, i), attackerName),
            GameEventType.AttackFailed => Pick(Failures, i),
            GameEventType.AttackRetreated => Pick(Retreats, i),
            GameEventType.PlayerEliminated => "A commander falls! One fewer rival in the race for the world.",
            GameEventType.GameWon => "History is written. The world has a single ruler!",
            GameEventType.GameLost => "The campaign is over. The player's last stronghold has fallen.",
            _ => e.Message
        };
    }

    private static string Pick(string[] lines, int i) => lines[i % lines.Length];
}
