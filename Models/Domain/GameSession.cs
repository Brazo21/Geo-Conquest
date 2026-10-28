namespace GeoConquest.Models.Domain;

/// <summary>
/// Aggregate root: the ENTIRE state of one game, stored as a single MongoDB document
/// (players and countries are embedded). One read loads a game; one write saves it.
///
/// Responsibility split:
///  • GameSession guards invariants (whose turn it is, what phase allows what, no soldiers
///    created or destroyed by accident).
///  • GameEngineService (Phase 2) owns the rules that DECIDE outcomes: dice, battle resolution,
///    reinforcement maths, NPC strategy. It calls these methods to apply its decisions.
/// </summary>
public sealed class GameSession
{
    public const int BoardSize = 10;
    public const int MinimumPlayers = 2;

    // Mapped explicitly as "players" / "countries" in MongoMappings; never exposed as mutable lists.
    private List<Player> _players = new();
    private List<Country> _countries = new();
    private List<GameEvent> _events = new();

    public string Id { get; private set; } = string.Empty;

    /// <summary>Identity user id of the account that created (and alone may play) this game.</summary>
    public string OwnerUserId { get; private set; } = string.Empty;

    public GameStatus Status { get; private set; }

    /// <summary>Starts at 1. Reinforcements are granted from round 2 onward.</summary>
    public int Round { get; private set; }

    public string? CurrentPlayerId { get; private set; }

    public TurnPhase Phase { get; private set; }

    /// <summary>Reinforcements granted this turn that have not yet been placed.</summary>
    public int PendingReinforcements { get; private set; }

    /// <summary>One attack per turn.</summary>
    public bool AttackUsedThisTurn { get; private set; }

    /// <summary>Set while a multi-roll attack is in progress, so it survives across HTTP requests.</summary>
    public string? ActiveAttackerCode { get; private set; }

    public string? ActiveDefenderCode { get; private set; }

    public string? WinnerPlayerId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>Optimistic-concurrency token, incremented on every save by the repository.</summary>
    public long Version { get; private set; }

    /// <summary>Sequence number of the newest event. Spectators poll for events after this.</summary>
    public long LastEventSequence { get; private set; }

    public IReadOnlyList<Player> Players => _players.AsReadOnly();

    public IReadOnlyList<Country> Countries => _countries.AsReadOnly();

    public IReadOnlyList<GameEvent> Events => _events.AsReadOnly();

    // Required by the MongoDB serializer; not usable by application code.
    private GameSession()
    {
    }

    // ───────────────────────────── Creation ─────────────────────────────

    public static GameSession Create(string ownerUserId, IEnumerable<Player> players, IEnumerable<Country> countries)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            throw new DomainException("A game must belong to a logged-in user.");
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(countries);

        var playerList = players.ToList();
        var countryList = countries.ToList();

        if (countryList.Count != BoardSize)
            throw new DomainException($"A game needs exactly {BoardSize} countries (received {countryList.Count}).");
        if (countryList.Select(c => c.Code).Distinct(StringComparer.Ordinal).Count() != BoardSize)
            throw new DomainException("Each country can only appear once on the board.");

        if (playerList.Count < MinimumPlayers || playerList.Count > BoardSize)
            throw new DomainException($"A game needs between {MinimumPlayers} and {BoardSize} players.");
        if (playerList.Select(p => p.PlayerId).Distinct(StringComparer.Ordinal).Count() != playerList.Count)
            throw new DomainException("Player ids must be unique.");

        var humans = playerList.Where(p => p.IsHuman()).ToList();
        if (humans.Count != 1 || !humans[0].IsControlledBy(ownerUserId))
            throw new DomainException("A single-player game needs exactly one human seat, owned by the game's creator.");

        var playerIds = playerList.Select(p => p.PlayerId).ToHashSet(StringComparer.Ordinal);
        if (countryList.Any(c => !playerIds.Contains(c.OwnerId)))
            throw new DomainException("Every country must be owned by a player in this game.");
        if (playerList.Any(p => countryList.All(c => !c.IsOwnedBy(p.PlayerId))))
            throw new DomainException("Every player must start with at least one country.");

        var now = DateTime.UtcNow;
        return new GameSession
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerUserId = ownerUserId,
            Status = GameStatus.Setup,
            Round = 0,
            Phase = TurnPhase.NotStarted,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 0,
            _players = playerList,
            _countries = countryList
        };
    }

    /// <summary>
    /// Starts play using a turn order decided (randomly) by the engine.
    /// The first player in the order moves first; round 1 has no reinforcements.
    /// </summary>
    public void Start(IReadOnlyList<string> turnOrderPlayerIds)
    {
        if (Status != GameStatus.Setup)
            throw new DomainException("This game has already started.");
        ArgumentNullException.ThrowIfNull(turnOrderPlayerIds);

        bool isPermutation = turnOrderPlayerIds.Count == _players.Count &&
                             turnOrderPlayerIds.Distinct(StringComparer.Ordinal).Count() == _players.Count &&
                             turnOrderPlayerIds.All(id => _players.Any(p => p.PlayerId == id));
        if (!isPermutation)
            throw new DomainException("The turn order must list every player exactly once.");

        for (int i = 0; i < turnOrderPlayerIds.Count; i++)
            GetPlayer(turnOrderPlayerIds[i]).AssignTurnOrder(i);

        Status = GameStatus.InProgress;
        Round = 1;
        CurrentPlayerId = turnOrderPlayerIds[0];
        ResetTurnState();
    }

    // ───────────────────────────── Queries ─────────────────────────────

    public bool IsOwnedBy(string? userId) =>
        !string.IsNullOrEmpty(userId) && string.Equals(OwnerUserId, userId, StringComparison.Ordinal);

    public Player GetPlayer(string playerId) =>
        _players.FirstOrDefault(p => p.PlayerId == playerId)
        ?? throw new DomainException("That player is not part of this game.");

    public Country GetCountry(string countryCode)
    {
        string code = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        return _countries.FirstOrDefault(c => c.Code == code)
               ?? throw new DomainException($"Country '{countryCode}' is not on this board.");
    }

    public Player GetCurrentPlayer() =>
        CurrentPlayerId is null
            ? throw new DomainException("The game has not started yet.")
            : GetPlayer(CurrentPlayerId);

    public Player GetHumanPlayer() => _players.Single(p => p.IsHuman());

    public IReadOnlyList<Country> GetCountriesOwnedBy(string playerId) =>
        _countries.Where(c => c.IsOwnedBy(playerId)).ToList().AsReadOnly();

    public IReadOnlyList<Player> GetActivePlayers() =>
        _players.Where(p => !p.IsEliminated).OrderBy(p => p.TurnOrder).ToList().AsReadOnly();

    public int GetTotalSoldiers(string playerId) =>
        _countries.Where(c => c.IsOwnedBy(playerId)).Sum(c => c.SoldierCount);

    /// <summary>True when this authenticated user may act right now (it is their human seat's turn).</summary>
    public bool CanUserAct(string? userId) =>
        Status == GameStatus.InProgress &&
        IsOwnedBy(userId) &&
        CurrentPlayerId is not null &&
        GetCurrentPlayer().IsControlledBy(userId);

    // ───────────────────────────── Turn flow ─────────────────────────────

    /// <summary>Records reinforcements calculated by the engine (2 per country from round 2).</summary>
    public void GrantReinforcements(int amount)
    {
        EnsureInProgress();
        if (amount < 0)
            throw new DomainException("Reinforcements cannot be negative.");
        if (Phase != TurnPhase.Action || AttackUsedThisTurn || PendingReinforcements != 0)
            throw new DomainException("Reinforcements can only be granted at the start of a turn.");

        PendingReinforcements = amount;
        Phase = amount > 0 ? TurnPhase.Reinforcement : TurnPhase.Action;
    }

    /// <summary>Places some of this turn's reinforcements on a country the current player owns.</summary>
    public void PlaceReinforcements(string playerId, string countryCode, int amount)
    {
        EnsureCurrentPlayer(playerId);
        if (Phase != TurnPhase.Reinforcement)
            throw new DomainException("There are no reinforcements to place right now.");
        if (amount <= 0 || amount > PendingReinforcements)
            throw new DomainException($"You can place between 1 and {PendingReinforcements} soldier(s).");

        var country = GetCountry(countryCode);
        if (!country.IsOwnedBy(playerId))
            throw new DomainException($"You do not control {country.Name}.");

        country.AddSoldiers(amount);
        PendingReinforcements -= amount;

        if (PendingReinforcements == 0)
            Phase = TurnPhase.Action;
    }

    /// <summary>Moves soldiers between two of the current player's countries (not during an attack).</summary>
    public void TransferSoldiers(string playerId, string fromCode, string toCode, int amount)
    {
        EnsureCurrentPlayer(playerId);
        if (Phase == TurnPhase.Attacking)
            throw new DomainException("You cannot transfer soldiers while an attack is in progress.");
        if (Phase == TurnPhase.Reinforcement)
            throw new DomainException("Place your reinforcements before transferring soldiers.");

        var from = GetCountry(fromCode);
        if (!from.IsOwnedBy(playerId))
            throw new DomainException($"You do not control {from.Name}.");

        from.TransferSoldiersTo(GetCountry(toCode), amount);
    }

    /// <summary>Opens the turn's single attack. Dice are rolled by the engine.</summary>
    public void BeginAttack(string playerId, string attackerCode, string defenderCode)
    {
        EnsureCurrentPlayer(playerId);
        if (Phase == TurnPhase.Reinforcement)
            throw new DomainException("Place your reinforcements before attacking.");
        if (Phase != TurnPhase.Action)
            throw new DomainException("An attack is already in progress.");
        if (AttackUsedThisTurn)
            throw new DomainException("You may only launch one attack per turn.");

        var attacker = GetCountry(attackerCode);
        var defender = GetCountry(defenderCode);

        if (!attacker.IsOwnedBy(playerId))
            throw new DomainException($"You do not control {attacker.Name}.");
        if (defender.IsOwnedBy(playerId))
            throw new DomainException($"You cannot attack your own country ({defender.Name}).");
        if (!attacker.CanLaunchAttack())
            throw new DomainException($"{attacker.Name} needs at least {Country.MinimumAttackingStrength} soldiers to attack.");

        ActiveAttackerCode = attacker.Code;
        ActiveDefenderCode = defender.Code;
        AttackUsedThisTurn = true;
        Phase = TurnPhase.Attacking;
    }

    public Country GetActiveAttacker() =>
        ActiveAttackerCode is null ? throw new DomainException("No attack is in progress.") : GetCountry(ActiveAttackerCode);

    public Country GetActiveDefender() =>
        ActiveDefenderCode is null ? throw new DomainException("No attack is in progress.") : GetCountry(ActiveDefenderCode);

    /// <summary>Closes the attack (conquest, failure or retreat). Transfers become available again.</summary>
    public void EndAttack()
    {
        EnsureInProgress();
        if (Phase != TurnPhase.Attacking)
            throw new DomainException("No attack is in progress.");

        ActiveAttackerCode = null;
        ActiveDefenderCode = null;
        Phase = TurnPhase.Action;
    }

    /// <summary>
    /// Passes the turn to the next non-eliminated player in turn order.
    /// The round counter advances each time play wraps back to the start of the order.
    /// </summary>
    public Player AdvanceTurn()
    {
        EnsureInProgress();
        if (Phase == TurnPhase.Attacking)
            throw new DomainException("Finish or retreat from the current attack before ending the turn.");

        var ordered = _players.OrderBy(p => p.TurnOrder).ToList();
        int currentIndex = ordered.FindIndex(p => p.PlayerId == CurrentPlayerId);

        for (int step = 1; step <= ordered.Count; step++)
        {
            int index = (currentIndex + step) % ordered.Count;
            var candidate = ordered[index];
            if (candidate.IsEliminated)
                continue;

            if (currentIndex + step >= ordered.Count)
                Round++;

            CurrentPlayerId = candidate.PlayerId;
            ResetTurnState();
            return candidate;
        }

        throw new DomainException("No active players remain.");
    }

    // ───────────────────────────── End conditions ─────────────────────────────

    public void EliminatePlayer(string playerId)
    {
        EnsureInProgress();
        var player = GetPlayer(playerId);
        if (_countries.Any(c => c.IsOwnedBy(playerId)))
            throw new DomainException($"{player.DisplayName} still controls territory and cannot be eliminated.");

        player.Eliminate(Round);
    }

    public void DeclareWinner(string playerId)
    {
        EnsureInProgress();
        var player = GetPlayer(playerId);
        if (!_countries.All(c => c.IsOwnedBy(playerId)))
            throw new DomainException($"{player.DisplayName} has not conquered all {BoardSize} countries.");

        WinnerPlayerId = playerId;
        Status = GameStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        Phase = TurnPhase.NotStarted;
        ActiveAttackerCode = null;
        ActiveDefenderCode = null;
    }

    /// <summary>Ends a single-player game in defeat once the human has lost every country.</summary>
    public void ConcludeWithHumanDefeat()
    {
        EnsureInProgress();
        if (!GetHumanPlayer().IsEliminated)
            throw new DomainException("The game can only end in defeat once the human player is eliminated.");

        WinnerPlayerId = null;
        Status = GameStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        Phase = TurnPhase.NotStarted;
        ActiveAttackerCode = null;
        ActiveDefenderCode = null;
    }

    public bool DidHumanWin() =>
        Status == GameStatus.Completed && WinnerPlayerId == GetHumanPlayer().PlayerId;

    public void Abandon()
    {
        if (Status == GameStatus.Completed)
            throw new DomainException("A finished game cannot be abandoned.");

        Status = GameStatus.Abandoned;
        Phase = TurnPhase.NotStarted;
    }

    // ───────────────────────────── Event log ─────────────────────────────

    /// <summary>Appends an event, stamping it with the next sequence number, the round and the time.</summary>
    public GameEvent AppendEvent(GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        LastEventSequence++;
        gameEvent.Stamp(LastEventSequence, Round, DateTime.UtcNow, CaptureBoard());
        _events.Add(gameEvent);
        return gameEvent;
    }

    /// <summary>Compact snapshot of every country: "CODE.playerIndex.soldiers", comma-separated.</summary>
    private string CaptureBoard() =>
        string.Join(',', _countries.Select(c =>
            $"{c.Code}.{_players.FindIndex(p => p.PlayerId == c.OwnerId)}.{c.SoldierCount}"));

    /// <summary>Events newer than <paramref name="sequence"/>, oldest first (used by live polling).</summary>
    public IReadOnlyList<GameEvent> GetEventsSince(long sequence) =>
        _events.Where(e => e.Sequence > sequence).OrderBy(e => e.Sequence).ToList().AsReadOnly();

    // ───────────────────────────── Persistence hook ─────────────────────────────

    /// <summary>Called only by the repository immediately before a write.</summary>
    internal void MarkSaved()
    {
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    // ───────────────────────────── Guards ─────────────────────────────

    private void ResetTurnState()
    {
        Phase = TurnPhase.Action;
        PendingReinforcements = 0;
        AttackUsedThisTurn = false;
        ActiveAttackerCode = null;
        ActiveDefenderCode = null;
    }

    private void EnsureInProgress()
    {
        if (Status != GameStatus.InProgress)
            throw new DomainException("This game is not in progress.");
    }

    private void EnsureCurrentPlayer(string playerId)
    {
        EnsureInProgress();
        if (!string.Equals(CurrentPlayerId, playerId, StringComparison.Ordinal))
            throw new DomainException("It is not your turn.");
    }
}
