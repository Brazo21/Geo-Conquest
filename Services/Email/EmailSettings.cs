namespace GeoConquest.Services.Email;

public sealed class EmailSettings
{
    public const string SectionName = "Email";
    public const string SmtpProvider = "Smtp";
    public const string BrevoProvider = "Brevo";

    public string Provider { get; init; } = SmtpProvider;

    public string ApiKey { get; init; } = string.Empty;

    public bool UsesBrevo => string.Equals(Provider, BrevoProvider, StringComparison.OrdinalIgnoreCase);

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public bool EnableSsl { get; init; } = true;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;

    public string FromName { get; init; } = "GeoConquest Command";

    public string PublicBaseUrl { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(FromAddress) &&
        (UsesBrevo ? !string.IsNullOrWhiteSpace(ApiKey) : !string.IsNullOrWhiteSpace(Host));
}
