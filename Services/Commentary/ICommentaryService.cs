using GeoConquest.Models.Domain;

namespace GeoConquest.Services.Commentary;

public interface ICommentaryService
{
    Task AnnotateAsync(GameSession session, long afterSequence, CancellationToken cancellationToken = default);
}
