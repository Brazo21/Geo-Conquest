namespace Geo_Conquest.Models;

/// <summary>What the friendly error page shows. Never contains technical details.</summary>
public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public int StatusCode { get; set; } = 500;

    public string Title { get; set; } = "Something went wrong";

    public string Message { get; set; } = "Our command room hit a snag. Please try again in a moment.";

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public static ErrorViewModel ForStatus(int code, string? requestId) => code switch
    {
        400 => new() { StatusCode = 400, RequestId = requestId, Title = "That request didn't look right", Message = "Something in the form or link was not valid. Go back and try again." },
        401 or 403 => new() { StatusCode = code, RequestId = requestId, Title = "You can't do that here", Message = "You can watch any game, but only the commander who started it can make moves. Log in or start your own game." },
        404 => new() { StatusCode = 404, RequestId = requestId, Title = "Page not found", Message = "That page or game doesn't exist. Check the link, or head back to the games list." },
        429 => new() { StatusCode = 429, RequestId = requestId, Title = "Too many attempts", Message = "You've tried that too many times in a short while. Wait a minute, then try again." },
        _ => new() { StatusCode = code is >= 400 and <= 599 ? code : 500, RequestId = requestId }
    };
}
