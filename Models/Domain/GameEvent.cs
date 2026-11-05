namespace GeoConquest.Models.Domain;

public sealed class GameEvent
{
    private List<int> _attackerDice = new();
    private List<int> _defenderDice = new();

    public long Sequence { get; private set; }

    public int Round { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public GameEventType Type { get; private set; }

    public string? ActorPlayerId { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public string? Reasoning { get; private set; }

    public string? FromCountryCode { get; private set; }

    public string? ToCountryCode { get; private set; }

    public int? AttackerRemaining { get; private set; }

    public int? DefenderRemaining { get; private set; }

    public double? WinOdds { get; private set; }

    public string? Commentary { get; private set; }

    public bool CommentaryIsAi { get; private set; }

    public string BoardSnapshot { get; private set; } = string.Empty;

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

    public bool HasCommentary => !string.IsNullOrEmpty(Commentary);

    public IReadOnlyList<BoardEntry> ReadBoard()
    {
        if (string.IsNullOrEmpty(BoardSnapshot))
            return Array.Empty<BoardEntry>();

        var entries = new List<BoardEntry>();
        foreach (string part in BoardSnapshot.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] bits = part.Split('.');
            if (bits.Length == 3 && int.TryParse(bits[1], out int owner) && int.TryParse(bits[2], out int soldiers))
                entries.Add(new BoardEntry(bits[0], owner, soldiers));
        }

        return entries;
    }

    public bool IsBattleEvent => Type is GameEventType.AttackStarted or GameEventType.DiceRolled
        or GameEventType.CountryConquered or GameEventType.AttackFailed or GameEventType.AttackRetreated
        or GameEventType.PlayerEliminated or GameEventType.GameWon or GameEventType.GameLost;

    public void AttachCommentary(string commentary, bool isAi)
    {
        if (string.IsNullOrWhiteSpace(commentary))
            throw new DomainException("Commentary cannot be empty.");
        if (HasCommentary)
            throw new DomainException("This event already has commentary.");

        string text = commentary.Trim();
        Commentary = text.Length > 400 ? text[..400] : text;
        CommentaryIsAi = isAi;
    }

    // Only the GameSession aggregate assigns position and time, so the log can't be reordered.
    internal void Stamp(long sequence, int round, DateTime occurredAtUtc, string boardSnapshot)
    {
        if (Sequence != 0)
            throw new DomainException("This event has already been recorded.");

        Sequence = sequence;
        Round = round;
        OccurredAtUtc = occurredAtUtc;
        BoardSnapshot = boardSnapshot;
    }
}

public sealed record BoardEntry(string Code, int OwnerIndex, int Soldiers);
