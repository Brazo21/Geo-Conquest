namespace GeoConquest.Models.Domain;

public sealed class Alliance
{
    public string Id { get; private set; } = string.Empty;

    public string PlayerAId { get; private set; } = string.Empty;

    public string PlayerBId { get; private set; } = string.Empty;

    public int FormedInRound { get; private set; }

    public int ExpiresAfterRound { get; private set; }

    public AllianceStatus Status { get; private set; }

    public string? BrokenByPlayerId { get; private set; }

    public int? EndedInRound { get; private set; }

    // Required by the MongoDB serializer; not usable by application code.
    private Alliance()
    {
    }

    public bool IsActive => Status == AllianceStatus.Active;

    public bool Includes(string playerId) => PlayerAId == playerId || PlayerBId == playerId;

    public string PartnerOf(string playerId) =>
        PlayerAId == playerId ? PlayerBId
        : PlayerBId == playerId ? PlayerAId
        : throw new DomainException("That commander is not part of this alliance.");

    internal static Alliance Form(string proposerId, string partnerId, int round, int lengthRounds)
    {
        if (string.IsNullOrWhiteSpace(proposerId) || string.IsNullOrWhiteSpace(partnerId) || proposerId == partnerId)
            throw new DomainException("An alliance needs two different commanders.");
        if (lengthRounds < 1)
            throw new DomainException("An alliance must last at least one round.");

        return new Alliance
        {
            Id = Guid.NewGuid().ToString("N"),
            PlayerAId = proposerId,
            PlayerBId = partnerId,
            FormedInRound = round,
            ExpiresAfterRound = round + lengthRounds,
            Status = AllianceStatus.Active
        };
    }

    internal void Break(string byPlayerId, int round)
    {
        EnsureActive();
        if (!Includes(byPlayerId))
            throw new DomainException("Only a member can break an alliance.");

        Status = AllianceStatus.Broken;
        BrokenByPlayerId = byPlayerId;
        EndedInRound = round;
    }

    internal void Expire(int round)
    {
        EnsureActive();
        Status = AllianceStatus.Expired;
        EndedInRound = round;
    }

    internal void End(int round)
    {
        EnsureActive();
        Status = AllianceStatus.Ended;
        EndedInRound = round;
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new DomainException("This alliance is no longer active.");
    }
}
