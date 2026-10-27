using System.Security.Cryptography;

namespace GeoConquest.Services;

/// <summary>
/// Source of randomness for dice, country draws and turn order.
/// Behind an interface so tests can supply fixed rolls.
/// </summary>
public interface IRandomSource
{
    /// <summary>Returns an integer in [minInclusive, maxExclusive).</summary>
    int Next(int minInclusive, int maxExclusive);
}

/// <summary>
/// Cryptographically strong, thread-safe randomness. Rolls happen only on the server
/// and cannot be predicted or influenced by the browser.
/// </summary>
public sealed class CryptoRandomSource : IRandomSource
{
    public int Next(int minInclusive, int maxExclusive) =>
        RandomNumberGenerator.GetInt32(minInclusive, maxExclusive);
}

public static class RandomSourceExtensions
{
    /// <summary>One six-sided die.</summary>
    public static int RollDie(this IRandomSource random) => random.Next(1, 7);

    /// <summary>Fisher–Yates shuffle into a new list.</summary>
    public static List<T> Shuffle<T>(this IRandomSource random, IEnumerable<T> items)
    {
        var list = items.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}
