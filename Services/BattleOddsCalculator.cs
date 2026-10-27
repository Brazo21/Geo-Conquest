using System.Collections.Concurrent;

namespace GeoConquest.Services;

public interface IBattleOddsCalculator
{
    /// <summary>
    /// Probability (0–1) that an attacker with <paramref name="attackerSoldiers"/> in total
    /// conquers a defender with <paramref name="defenderSoldiers"/>, if it keeps rolling until
    /// the defender hits 0 or the attacker is down to 1.
    /// </summary>
    double AttackerWinProbability(int attackerSoldiers, int defenderSoldiers);
}

/// <summary>
/// Exact odds for GeoConquest's dice rules (not Risk's): each roll uses
/// n = min(attacker − 1, defender) dice per side, dice are paired in order,
/// higher die wins, ties change nothing. For one pair:
///   P(attacker wins) = 15/36, P(defender wins) = 15/36, P(tie) = 6/36.
/// A roll with k attacker losses, j defender losses and t ties has multinomial probability.
/// The all-tie outcome returns to the same state, so it is divided out.
/// Results are memoised; armies above <see cref="ExactLimit"/> are scaled down proportionally
/// (for big armies the odds depend almost only on the ratio).
/// </summary>
public sealed class BattleOddsCalculator : IBattleOddsCalculator
{
    private const int ExactLimit = 60;
    private const double PairWin = 15.0 / 36.0;
    private const double PairLoss = 15.0 / 36.0;
    private const double PairTie = 6.0 / 36.0;

    private static readonly double LogWin = Math.Log(PairWin);
    private static readonly double LogLoss = Math.Log(PairLoss);
    private static readonly double LogTie = Math.Log(PairTie);
    private static readonly double[] LogFactorial = BuildLogFactorials(ExactLimit + 1);

    private readonly ConcurrentDictionary<(int Attackers, int Defenders), double> _cache = new();

    public double AttackerWinProbability(int attackerSoldiers, int defenderSoldiers)
    {
        if (defenderSoldiers <= 0) return 1.0;
        if (attackerSoldiers <= 1) return 0.0;

        (int a, int d) = ScaleDown(attackerSoldiers, defenderSoldiers);
        return Solve(a, d);
    }

    private double Solve(int attackers, int defenders)
    {
        if (defenders <= 0) return 1.0;
        if (attackers <= 1) return 0.0;
        if (_cache.TryGetValue((attackers, defenders), out double cached)) return cached;

        int dice = Math.Min(attackers - 1, defenders);
        double allTies = Math.Exp(dice * LogTie);
        double total = 0.0;

        // k = attacker soldiers lost, j = defender soldiers lost, the rest are ties.
        for (int k = 0; k <= dice; k++)
        {
            for (int j = 0; j + k <= dice; j++)
            {
                if (k == 0 && j == 0) continue; // the all-tie self-loop, handled below

                int ties = dice - k - j;
                double logP = LogFactorial[dice] - LogFactorial[k] - LogFactorial[j] - LogFactorial[ties]
                              + j * LogWin + k * LogLoss + ties * LogTie;

                total += Math.Exp(logP) * Solve(attackers - k, defenders - j);
            }
        }

        double result = total / (1.0 - allTies);
        _cache.TryAdd((attackers, defenders), result);
        return result;
    }

    private static (int, int) ScaleDown(int attackers, int defenders)
    {
        int largest = Math.Max(attackers, defenders);
        if (largest <= ExactLimit) return (attackers, defenders);

        double factor = (double)ExactLimit / largest;
        int a = Math.Max(2, (int)Math.Round(attackers * factor));
        int d = Math.Max(1, (int)Math.Round(defenders * factor));
        return (a, d);
    }

    private static double[] BuildLogFactorials(int size)
    {
        var table = new double[size + 1];
        for (int i = 2; i <= size; i++)
            table[i] = table[i - 1] + Math.Log(i);
        return table;
    }
}
