namespace GeoConquest.Models.Domain;

/// <summary>
/// A territory on the 10-country board. Embedded inside <see cref="GameSession"/>.
/// All state is mutated only through intention-revealing methods, so a country
/// can never go negative, change hands while defended, or be stripped below its garrison.
/// </summary>
public sealed class Country
{
    public const int MinimumGarrison = 1;
    public const int MinimumAttackingStrength = 2;
    public const int MinimumConquestReward = 2;
    private const int MaximumSoldierValue = 9;

    // Population band (inclusive upper bound) → soldier value, as specified in the exam brief.
    private static readonly (long MaxPopulation, int Soldiers)[] SoldierTable =
    {
        (5_000_000, 1),
        (10_000_000, 2),
        (25_000_000, 3),
        (50_000_000, 4),
        (75_000_000, 5),
        (100_000_000, 6),
        (125_000_000, 7),
        (150_000_000, 8)
    };

    /// <summary>ISO 3166-1 alpha-2/3 code, unique within a game (e.g. "ZA").</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public long Population { get; private set; }

    /// <summary>Fixed, population-derived value (1–9). Used for starting strength and conquest spoils.</summary>
    public int SoldierValue { get; private set; }

    /// <summary>Soldiers currently stationed here. Changes during play.</summary>
    public int SoldierCount { get; private set; }

    /// <summary>The <see cref="Player.PlayerId"/> of the current owner.</summary>
    public string OwnerId { get; private set; } = string.Empty;

    // Required by the MongoDB serializer; not usable by application code.
    private Country()
    {
    }

    public static Country Create(string code, string name, long population, string ownerId)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A country code is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("A country name is required.");
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new DomainException($"{name} must be assigned to a player.");

        int soldierValue = CalculateSoldierValue(population);

        return new Country
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Population = population,
            SoldierValue = soldierValue,
            SoldierCount = soldierValue,
            OwnerId = ownerId
        };
    }

    /// <summary>Maps a population to a soldier value using the brief's table.</summary>
    public static int CalculateSoldierValue(long population)
    {
        if (population <= 0)
            throw new DomainException("Population must be greater than zero.");

        foreach (var (maxPopulation, soldiers) in SoldierTable)
        {
            if (population <= maxPopulation)
                return soldiers;
        }

        return MaximumSoldierValue;
    }

    public bool IsOwnedBy(string playerId) =>
        string.Equals(OwnerId, playerId, StringComparison.Ordinal);

    public bool IsDefeated() => SoldierCount == 0;

    /// <summary>An attack needs at least 2 soldiers, because 1 must always stay home.</summary>
    public bool CanLaunchAttack() => SoldierCount >= MinimumAttackingStrength;

    /// <summary>Soldiers that may fight (total minus the one that stays behind).</summary>
    public int GetAvailableAttackers() => Math.Max(0, SoldierCount - MinimumGarrison);

    /// <summary>Spoils for conquering this country: Max(2, soldier value / 2), rounded down.</summary>
    public int GetConquestReward() => Math.Max(MinimumConquestReward, SoldierValue / 2);

    public void AddSoldiers(int amount)
    {
        if (amount <= 0)
            throw new DomainException("You must add at least one soldier.");

        SoldierCount = checked(SoldierCount + amount);
    }

    /// <summary>Removes battle casualties. May reach zero (the country is then defeated).</summary>
    public void RemoveSoldiers(int amount)
    {
        if (amount <= 0)
            throw new DomainException("Casualties must be at least one soldier.");
        if (amount > SoldierCount)
            throw new DomainException($"{Name} only has {SoldierCount} soldier(s) to lose.");

        SoldierCount -= amount;
    }

    /// <summary>Hands the country to its conqueror. Only legal once its garrison has been wiped out.</summary>
    public void ChangeOwner(string newOwnerId)
    {
        if (string.IsNullOrWhiteSpace(newOwnerId))
            throw new DomainException("A new owner is required.");
        if (IsOwnedBy(newOwnerId))
            throw new DomainException($"{Name} is already controlled by that player.");
        if (!IsDefeated())
            throw new DomainException($"{Name} cannot change hands while it still has {SoldierCount} soldier(s).");

        OwnerId = newOwnerId;
    }

    /// <summary>Moves soldiers between two countries held by the same owner, keeping at least 1 behind.</summary>
    public void TransferSoldiersTo(Country destination, int amount)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (ReferenceEquals(this, destination) || destination.Code == Code)
            throw new DomainException("Choose two different countries for a transfer.");
        if (!destination.IsOwnedBy(OwnerId))
            throw new DomainException("You can only transfer soldiers between countries you control.");
        if (amount <= 0)
            throw new DomainException("Transfer at least one soldier.");
        if (SoldierCount - amount < MinimumGarrison)
            throw new DomainException(
                $"{Name} must keep at least {MinimumGarrison} soldier. You can move at most {GetAvailableAttackers()}.");

        SoldierCount -= amount;
        destination.SoldierCount = checked(destination.SoldierCount + amount);
    }
}
