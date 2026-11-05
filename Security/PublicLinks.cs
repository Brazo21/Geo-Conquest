using GeoConquest.Services.Email;

namespace GeoConquest.Security;

/// <summary>
/// Builds absolute links for emails. Uses Email:PublicBaseUrl when set. Only in development does it fall back
/// to the request's own address: in production a faked Host header could otherwise point reset links at an
/// attacker's site ("host header injection"), so no link is made at all.
/// </summary>
public static class PublicLinks
{
    public static string? Build(HttpRequest request, EmailSettings settings, IWebHostEnvironment env, string pathAndQuery)
    {
        string? baseUrl = !string.IsNullOrWhiteSpace(settings.PublicBaseUrl)
            ? settings.PublicBaseUrl.TrimEnd('/')
            : env.IsDevelopment() ? $"{request.Scheme}://{request.Host}" : null;

        if (baseUrl is null || !Uri.TryCreate(baseUrl + pathAndQuery, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
            return null;

        return uri.ToString();
    }
}
