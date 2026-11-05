using GeoConquest.Services.Email;

namespace GeoConquest.Security;

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
