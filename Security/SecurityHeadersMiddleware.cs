using System.Security.Cryptography;

namespace GeoConquest.Security;

/// <summary>
/// Adds security headers to every response:
/// • Content-Security-Policy with a fresh per-request nonce: only our own scripts, scripts carrying the
///   nonce, and two pinned CDNs can run, which blocks injected (XSS) scripts.
/// • Clickjacking protection (frame-ancestors 'none' + X-Frame-Options), no MIME sniffing,
///   a strict referrer policy and no access to camera/microphone/location.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    public const string NonceKey = "csp-nonce";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Error pages re-run the pipeline for the same request: keep one nonce and one set of headers.
        if (context.Items[NonceKey] is string)
            return _next(context);

        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceKey] = nonce;

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                $"script-src 'self' 'nonce-{nonce}' https://cdnjs.cloudflare.com; " +
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
                "font-src 'self' https://fonts.gstatic.com; " +
                "img-src 'self' data: https://cdn.jsdelivr.net; " +
                "connect-src 'self' https://cdn.jsdelivr.net; " +
                "frame-ancestors 'none'; form-action 'self'; base-uri 'self'; object-src 'none'";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            return Task.CompletedTask;
        });

        return _next(context);
    }
}

public static class SecurityExtensions
{
    /// <summary>The nonce that inline &lt;script&gt; tags must carry to be allowed to run.</summary>
    public static string CspNonce(this HttpContext context) =>
        context.Items[SecurityHeadersMiddleware.NonceKey] as string ?? string.Empty;
}

/// <summary>Input checks shared by controllers (defence in depth on top of typed MongoDB filters).</summary>
public static class InputGuard
{
    public const int MaxSearchLength = 60;

    /// <summary>Game ids are 32 lowercase hex characters (a GUID without dashes).</summary>
    public static bool IsGameId(string? id) =>
        id is { Length: 32 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string? CleanSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;

        string trimmed = search.Trim();
        return trimmed.Length > MaxSearchLength ? trimmed[..MaxSearchLength] : trimmed;
    }
}
