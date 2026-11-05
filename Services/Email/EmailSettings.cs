namespace GeoConquest.Services.Email;

/// <summary>
/// "Email" section of appsettings.json. Keep passwords and API keys in User Secrets / host environment variables.
/// Provider "Smtp" (e.g. Gmail on your own PC: Host smtp.gmail.com, Port 587, a Google app password) or
/// "Brevo" (HTTP API, for hosts that block SMTP ports such as Render's free tier: ApiKey + a verified FromAddress).
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";
    public const string SmtpProvider = "Smtp";
    public const string BrevoProvider = "Brevo";

    public string Provider { get; init; } = SmtpProvider;

    /// <summary>Brevo API key (Brevo → SMTP &amp; API → API keys).</summary>
    public string ApiKey { get; init; } = string.Empty;

    public bool UsesBrevo => string.Equals(Provider, BrevoProvider, StringComparison.OrdinalIgnoreCase);

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public bool EnableSsl { get; init; } = true;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;

    public string FromName { get; init; } = "GeoConquest Command";

    /// <summary>
    /// The site's public address, e.g. https://geoconquest.example.com. Links in emails are built from this,
    /// never from the request's Host header (which an attacker could fake to steal reset links).
    /// Left empty in development, where the local address is used.
    /// </summary>
    public string PublicBaseUrl { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(FromAddress) &&
        (UsesBrevo ? !string.IsNullOrWhiteSpace(ApiKey) : !string.IsNullOrWhiteSpace(Host));
}
