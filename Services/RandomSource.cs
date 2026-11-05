using System.Security.Cryptography;

namespace GeoConquest.Services;

public interface IRandomSource
{
    int Next(int minInclusive, int maxExclusive);
}

public sealed class CryptoRandomSource : IRandomSource
{
    public int Next(int minInclusive, int maxExclusive) =>
        RandomNumberGenerator.GetInt32(minInclusive, maxExclusive);
}

public static class RandomSourceExtensions
{
    public static int RollDie(this IRandomSource random) => random.Next(1, 7);

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
