using GeoConquest.Models.Domain;

namespace GeoConquest.Models.ViewModels;

/// <summary>
/// JSON returned by GET /game/{id}/events?after=N. The browser uses it to replay turns step by step
/// (with fast-forward) and spectators poll it to follow a game live. Read-only and safe for anyone.
/// </summary>
public sealed record GameFeedDto(
    string GameId,
    GameStatus Status,
    int Round,
    string? CurrentPlayer,
    bool CanAct,
    long LastSequence,
    IReadOnlyList<FeedPlayerDto> Players,
    IReadOnlyDictionary<string, string> CountryNames,
    IReadOnlyList<FeedEventDto> Events)
{
    public const int MaxEventsPerResponse = 300;

    public static GameFeedDto From(GameSession session, long after, string? userId)
    {
        var players = session.Players.ToList();
        var indexById = players.Select((p, i) => (p.PlayerId, i)).ToDictionary(x => x.PlayerId, x => x.i);

        var events = session.GetEventsSince(Math.Max(0, after))
            .Take(MaxEventsPerResponse)
            .Select(e => new FeedEventDto(
                e.Sequence,
                e.Round,
                e.Type,
                e.Message,
                e.Reasoning,
                e.Commentary,
                e.CommentaryIsAi,
                e.ActorPlayerId is not null && indexById.TryGetValue(e.ActorPlayerId, out int actor) ? actor : null,
                e.FromCountryCode,
                e.ToCountryCode,
                e.AttackerDice,
                e.DefenderDice,
                e.WinOdds,
                e.BoardSnapshot))
            .ToList();

        return new GameFeedDto(
            session.Id,
            session.Status,
            session.Round,
            session.Status == GameStatus.InProgress ? session.GetCurrentPlayer().DisplayName : null,
            session.CanUserAct(userId),
            session.LastEventSequence,
            players.Select(p => new FeedPlayerDto(p.PlayerId, p.DisplayName, p.ColorHex, p.IsHuman(), p.IsEliminated)).ToList(),
            session.Countries.ToDictionary(c => c.Code, c => c.Name),
            events);
    }
}

public sealed record FeedPlayerDto(string Id, string Name, string Color, bool IsHuman, bool IsEliminated);

public sealed record FeedEventDto(
    long Seq,
    int Round,
    GameEventType Type,
    string Message,
    string? Reasoning,
    string? Commentary,
    bool CommentaryIsAi,
    int? Actor,
    string? From,
    string? To,
    IReadOnlyList<int> AttackerDice,
    IReadOnlyList<int> DefenderDice,
    double? WinOdds,
    string Board);
