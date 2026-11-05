namespace GeoConquest.Models.Domain;

public enum GameStatus
{
    Setup,
    InProgress,
    Completed,
    Abandoned
}

public enum PlayerType
{
    Human,
    Npc
}

public enum NpcProfile
{
    None,
    Aggressor,
    Defender,
    Opportunist
}

public enum TurnPhase
{
    NotStarted,
    Reinforcement,
    Action,
    Attacking
}

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
    GameLost,
    AllianceFormed,
    AllianceRejected,
    AllianceBroken,
    AllianceExpired,
    TurnTimedOut
}

public enum AllianceStatus
{
    Active,
    Expired,
    Broken,
    Ended
}
