using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace GeoConquest.Services.Email;

/// <summary>An email waiting to be sent. Bodies are plain text (nothing to inject into).</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>Queues emails so web requests never wait for the mail server.</summary>
public interface IEmailQueue
{
    bool IsConfigured { get; }

    void Enqueue(EmailMessage message);
}

/// <summary>
/// Bounded in-memory queue. Sending happens in <see cref="EmailSenderBackgroundService"/>, so the
/// "forgot password" page answers in the same time whether or not the account exists.
/// </summary>
public sealed class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel =
        Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly EmailSettings _settings;

    public EmailQueue(IOptions<EmailSettings> settings)
    {
        _settings = settings.Value;
    }

    public bool IsConfigured => _settings.IsConfigured;

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _channel.Writer.TryWrite(message);
    }
}

/// <summary>
/// Sends queued emails over SMTP or the Brevo HTTP API. Without settings it only logs that a message was due.
/// </summary>
public sealed class EmailSenderBackgroundService : BackgroundService
{
    public const string BrevoClientName = "brevo";

    private readonly EmailQueue _queue;
    private readonly EmailSettings _settings;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<EmailSenderBackgroundService> _logger;

    public EmailSenderBackgroundService(EmailQueue queue, IOptions<EmailSettings> settings, IHttpClientFactory http,
        ILogger<EmailSenderBackgroundService> logger)
    {
        _queue = queue;
        _settings = settings.Value;
        _http = http;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.IsConfigured)
            _logger.LogWarning("Email is OFF: Email settings incomplete. Password-reset emails will not be delivered.");
        else
            _logger.LogInformation("Email enabled via {Provider}.", _settings.UsesBrevo ? "Brevo API" : "SMTP");

        await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_settings.IsConfigured)
            {
                // Never log the body: it may contain a reset link.
                _logger.LogWarning("Email to {To} ({Subject}) not sent: email is not configured.", Mask(message.To), message.Subject);
                continue;
            }

            try
            {
                if (_settings.UsesBrevo)
                {
                    await SendWithBrevoAsync(message, stoppingToken);
                    _logger.LogInformation("Email sent to {To}: {Subject}", Mask(message.To), message.Subject);
                    continue;
                }

                using var mail = new MailMessage
                {
                    From = new MailAddress(_settings.FromAddress, _settings.FromName),
                    Subject = message.Subject,
                    Body = message.Body,
                    IsBodyHtml = false
                };
                mail.To.Add(message.To);

                using var smtp = new SmtpClient(_settings.Host, _settings.Port)
                {
                    EnableSsl = _settings.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Credentials = string.IsNullOrWhiteSpace(_settings.Username)
                        ? null
                        : new NetworkCredential(_settings.Username, _settings.Password),
                    Timeout = 20000
                };
                await smtp.SendMailAsync(mail, stoppingToken);
                _logger.LogInformation("Email sent to {To}: {Subject}", Mask(message.To), message.Subject);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not send email to {To}", Mask(message.To));
            }
        }
    }

    /// <summary>Brevo transactional email API (HTTPS, so it works where SMTP ports are blocked).</summary>
    private async Task SendWithBrevoAsync(EmailMessage message, CancellationToken ct)
    {
        var client = _http.CreateClient(BrevoClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = _settings.FromName, email = _settings.FromAddress },
                to = new[] { new { email = message.To } },
                subject = message.Subject,
                textContent = message.Body
            })
        };
        request.Headers.Add("api-key", _settings.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Brevo's error text explains problems like an unverified sender; it never contains the key.
            string detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Brevo returned {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 300)]}");
        }
    }

    /// <summary>Logs "ka***@gmail.com" rather than the full address (POPIA: minimum personal data in logs).</summary>
    private static string Mask(string email)
    {
        int at = email.IndexOf('@');
        return at <= 2 ? "***" + email[Math.Max(0, at)..] : email[..2] + "***" + email[at..];
    }
}
