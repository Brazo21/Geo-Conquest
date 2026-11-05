namespace GeoConquest.Models.Domain;

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

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public long Population { get; private set; }

    public int SoldierValue { get; private set; }

    public int SoldierCount { get; private set; }

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

    public bool CanLaunchAttack() => SoldierCount >= MinimumAttackingStrength;

    public int GetAvailableAttackers() => Math.Max(0, SoldierCount - MinimumGarrison);

    public int GetConquestReward() => Math.Max(MinimumConquestReward, SoldierValue / 2);

    public void AddSoldiers(int amount)
    {
        if (amount <= 0)
            throw new DomainException("You must add at least one soldier.");

        SoldierCount = checked(SoldierCount + amount);
    }

    public void RemoveSoldiers(int amount)
    {
        if (amount <= 0)
            throw new DomainException("Casualties must be at least one soldier.");
        if (amount > SoldierCount)
            throw new DomainException($"{Name} only has {SoldierCount} soldier(s) to lose.");

        SoldierCount -= amount;
    }

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
