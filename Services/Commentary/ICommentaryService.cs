using GeoConquest.Models.Domain;

namespace GeoConquest.Services.Commentary;

/// <summary>
/// Adds live commentary to the battle events produced by one move (or one run of NPC turns).
/// Never throws: if the AI is unavailable, fallback lines are used so the game always continues.
/// </summary>
public interface ICommentaryService
{
    Task AnnotateAsync(GameSession session, long afterSequence, CancellationToken cancellationToken = default);
}
