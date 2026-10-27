using System.Text.RegularExpressions;

namespace GeoConquest.Models.Domain;

/// <summary>
/// A seat in the game: the logged-in human or an NPC. Embedded inside <see cref="GameSession"/>.
/// Territory is NOT stored here; ownership lives on <see cref="Country.OwnerId"/> so there is a
/// single source of truth and the two can never drift apart.
/// </summary>
public sealed class Player
{
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public string PlayerId { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public PlayerType Type { get; private set; }

    /// <summary>ASP.NET Identity user id for the human seat; null for NPCs.</summary>
    public string? UserId { get; private set; }

    public NpcProfile Profile { get; private set; }

    /// <summary>Map/UI colour, e.g. "#3DDC84".</summary>
    public string ColorHex { get; private set; } = "#FFFFFF";

    /// <summary>0-based position in the randomised turn order.</summary>
    public int TurnOrder { get; private set; }

    public bool IsEliminated { get; private set; }

    public int? EliminatedInRound { get; private set; }

    // Required by the MongoDB serializer; not usable by application code.
    private Player()
    {
    }

    public static Player CreateHuman(string userId, string displayName, string colorHex)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("A human player must be linked to a logged-in user.");

        return new Player
        {
            PlayerId = NewId(),
            DisplayName = RequireName(displayName),
            Type = PlayerType.Human,
            UserId = userId,
            Profile = NpcProfile.None,
            ColorHex = RequireColor(colorHex)
        };
    }

    public static Player CreateNpc(string displayName, NpcProfile profile, string colorHex)
    {
        if (profile == NpcProfile.None)
            throw new DomainException("An NPC needs a profile (Aggressor, Defender or Opportunist).");

        return new Player
        {
            PlayerId = NewId(),
            DisplayName = RequireName(displayName),
            Type = PlayerType.Npc,
            UserId = null,
            Profile = profile,
            ColorHex = RequireColor(colorHex)
        };
    }

    public bool IsHuman() => Type == PlayerType.Human;

    /// <summary>True only for the human seat belonging to this authenticated user.</summary>
    public bool IsControlledBy(string? userId) =>
        IsHuman() && !string.IsNullOrEmpty(userId) && string.Equals(UserId, userId, StringComparison.Ordinal);

    // Only the GameSession aggregate root may change turn order or eliminate a player.
    internal void AssignTurnOrder(int order)
    {
        if (order < 0)
            throw new DomainException("Turn order cannot be negative.");

        TurnOrder = order;
    }

    internal void Eliminate(int round)
    {
        if (IsEliminated)
            throw new DomainException($"{DisplayName} has already been eliminated.");

        IsEliminated = true;
        EliminatedInRound = round;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string RequireName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("A player name is required.");

        string trimmed = displayName.Trim();
        if (trimmed.Length > 40)
            throw new DomainException("Player names must be 40 characters or fewer.");

        return trimmed;
    }

    private static string RequireColor(string colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex) || !HexColor.IsMatch(colorHex))
            throw new DomainException("Player colour must be a hex value like #3DDC84.");

        return colorHex.ToUpperInvariant();
    }
}
