namespace GeoConquest.Models.Domain;

/// <summary>Lifecycle of a game session.</summary>
public enum GameStatus
{
    Setup,
    InProgress,
    Completed,
    Abandoned
}

/// <summary>Who controls a player seat.</summary>
public enum PlayerType
{
    Human,
    Npc
}

/// <summary>NPC personality used by the decision engine (Aggressor / Defender / Opportunist).</summary>
public enum NpcProfile
{
    None,
    Aggressor,
    Defender,
    Opportunist
}

/// <summary>Where the current player is inside their turn.</summary>
public enum TurnPhase
{
    NotStarted,
    Reinforcement,
    Action,
    Attacking
}

/// <summary>Kinds of entries in the game's event log (the feed spectators and the replay read).</summary>
public enum GameEventType
{
    GameStarted,
    TurnStarted,
    Reinforced,
    Transferred,
    AttackStarted,
    DiceRolled,
    CountryConquered,
    AttackFailed,
    AttackRetreated,
    AttackSkipped,
    PlayerEliminated,
    GameWon,
    GameLost
}
