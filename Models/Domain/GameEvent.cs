namespace GeoConquest.Models.Domain;

/// <summary>
/// One entry in a game's event log, embedded in the <see cref="GameSession"/> document.
/// The log drives the live feed, the dice display, NPC "why" explanations, spectator
/// polling (by <see cref="Sequence"/>) and, later, AI commentary and replays.
/// </summary>
public sealed class GameEvent
{
    private List<int> _attackerDice = new();
    private List<int> _defenderDice = new();

    /// <summary>1, 2, 3… in order. Clients ask for "everything after sequence N".</summary>
    public long Sequence { get; private set; }

    public int Round { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public GameEventType Type { get; private set; }

    public string? ActorPlayerId { get; private set; }

    /// <summary>What happened, ready to show in the event log.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>For NPC actions: the one-sentence justification shown to the player.</summary>
    public string? Reasoning { get; private set; }

    public string? FromCountryCode { get; private set; }

    public string? ToCountryCode { get; private set; }

    public int? AttackerRemaining { get; private set; }

    public int? DefenderRemaining { get; private set; }

    /// <summary>Attacker's chance of conquering (0–1) at this moment, if relevant.</summary>
    public double? WinOdds { get; private set; }

    /// <summary>LLM-generated battle commentary, attached after the event is recorded.</summary>
    public string? Commentary { get; private set; }

    public IReadOnlyList<int> AttackerDice => _attackerDice.AsReadOnly();

    public IReadOnlyList<int> DefenderDice => _defenderDice.AsReadOnly();

    // Required by the MongoDB serializer; not usable by application code.
    private GameEvent()
    {
    }

    public static GameEvent Create(
        GameEventType type,
        string message,
        string? actorPlayerId = null,
        string? reasoning = null,
        string? fromCountryCode = null,
        string? toCountryCode = null,
        double? winOdds = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException("An event needs a message.");

        return new GameEvent
        {
            Type = type,
            Message = message.Trim(),
            ActorPlayerId = actorPlayerId,
            Reasoning = string.IsNullOrWhiteSpace(reasoning) ? null : reasoning.Trim(),
            FromCountryCode = fromCountryCode,
            ToCountryCode = toCountryCode,
            WinOdds = winOdds
        };
    }

    public static GameEvent DiceRoll(
        string actorPlayerId,
        string fromCountryCode,
        string toCountryCode,
        IEnumerable<int> attackerDice,
        IEnumerable<int> defenderDice,
        int attackerRemaining,
        int defenderRemaining,
        double? winOddsAfterRoll,
        string message)
    {
        var attack = attackerDice.ToList();
        var defend = defenderDice.ToList();

        if (attack.Count == 0 || attack.Count != defend.Count)
            throw new DomainException("Each attacking die must be paired with one defending die.");
        if (attack.Concat(defend).Any(d => d is < 1 or > 6))
            throw new DomainException("Dice values must be between 1 and 6.");

        var e = Create(GameEventType.DiceRolled, message, actorPlayerId, null, fromCountryCode, toCountryCode, winOddsAfterRoll);
        e._attackerDice = attack;
        e._defenderDice = defend;
        e.AttackerRemaining = attackerRemaining;
        e.DefenderRemaining = defenderRemaining;
        return e;
    }

    /// <summary>Attaches AI commentary once (the commentary service fills this in after a roll).</summary>
    public void AttachCommentary(string commentary)
    {
        if (string.IsNullOrWhiteSpace(commentary))
            throw new DomainException("Commentary cannot be empty.");

        Commentary = commentary.Trim();
    }

    // Only the GameSession aggregate assigns position and time, so the log can't be reordered.
    internal void Stamp(long sequence, int round, DateTime occurredAtUtc)
    {
        if (Sequence != 0)
            throw new DomainException("This event has already been recorded.");

        Sequence = sequence;
        Round = round;
        OccurredAtUtc = occurredAtUtc;
    }
}
