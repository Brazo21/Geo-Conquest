using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoConquest.Models.Domain;
using Microsoft.Extensions.Options;

namespace GeoConquest.Services.Commentary;

/// <summary>
/// Generates sports-style battle commentary with an LLM through an OpenAI-compatible
/// chat-completions endpoint (Gemini by default). All battle events from one request are
/// sent in a single call, and the model replies with one line per event as JSON.
/// Any event the AI misses (or every event, if the call fails) gets a fallback line.
/// </summary>
public sealed class LlmCommentaryService : ICommentaryService
{
    public const string HttpClientName = "commentary";

    private const string SystemPrompt =
        "You are the live commentator for GeoConquest, a Risk-style game of world conquest. " +
        "For each numbered battle event you receive, write ONE vivid, energetic sports-commentary line " +
        "(max 20 words) describing that moment as it happens. Use only the facts given: never invent " +
        "countries, numbers or outcomes. Refer to players by their names. Vary your wording, build " +
        "tension across a battle, and react to big swings, upsets and conquests. " +
        "Reply with JSON only, in exactly this shape: {\"lines\":[{\"seq\":123,\"text\":\"...\"}]}.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CommentarySettings _settings;
    private readonly ILogger<LlmCommentaryService> _logger;

    public LlmCommentaryService(IHttpClientFactory httpClientFactory, IOptions<CommentarySettings> settings,
        ILogger<LlmCommentaryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task AnnotateAsync(GameSession session, long afterSequence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var pending = session.GetEventsSince(afterSequence)
            .Where(e => e.IsBattleEvent && !e.HasCommentary)
            .ToList();
        if (pending.Count == 0)
            return;

        var forAi = SelectForAi(pending, Math.Max(1, _settings.MaxEventsPerRequest));

        if (_settings.IsConfigured)
        {
            var lines = await RequestWithFallbacksAsync(session, forAi, cancellationToken);
            foreach (var e in forAi)
            {
                if (lines.TryGetValue(e.Sequence, out var text) && !string.IsNullOrWhiteSpace(text))
                    e.AttachCommentary(text, isAi: true);
            }
        }

        foreach (var e in pending.Where(e => !e.HasCommentary))
        {
            var (attacker, defender) = Names(session, e);
            e.AttachCommentary(FallbackCommentary.For(e, attacker, defender), isAi: false);
        }
    }

    /// <summary>
    /// Keeps AI requests small (and fast): every decisive moment first (attacks, conquests, failures,
    /// retreats, eliminations), newest first, then the most recent dice rolls. Returned oldest first.
    /// </summary>
    private static List<GameEvent> SelectForAi(IReadOnlyList<GameEvent> pending, int max)
    {
        var decisive = pending.Where(e => e.Type != GameEventType.DiceRolled).OrderByDescending(e => e.Sequence);
        var rolls = pending.Where(e => e.Type == GameEventType.DiceRolled).OrderByDescending(e => e.Sequence);

        return decisive.Concat(rolls)
            .Take(max)
            .OrderBy(e => e.Sequence)
            .ToList();
    }

    /// <summary>
    /// Tries the main model, then each fallback model, within one overall time budget.
    /// Busy (503), rate-limited (429), missing (404) or slow models are skipped. Never throws
    /// for AI problems: an empty result means every event gets a fallback line.
    /// </summary>
    private async Task<Dictionary<long, string>> RequestWithFallbacksAsync(GameSession session,
        IReadOnlyList<GameEvent> events, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_settings.TimeoutSeconds, 2, 60)));

        foreach (string model in _settings.ModelsInOrder)
        {
            if (budget.IsCancellationRequested)
                break;

            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            attempt.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_settings.PerModelTimeoutSeconds, 2, 60)));

            try
            {
                var lines = await RequestLinesAsync(session, events, model, attempt.Token);
                if (lines.Count > 0)
                {
                    _logger.LogInformation("Commentary: {Count} AI line(s) from {Model}", lines.Count, model);
                    return lines;
                }

                _logger.LogWarning("Commentary: {Model} returned no usable lines; trying the next model", model);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Commentary: {Model} was too slow; trying the next model", model);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
            {
                _logger.LogWarning("Commentary: {Model} failed ({Reason}); trying the next model", model, ex.Message);
            }
        }

        _logger.LogWarning("Commentary: no AI model answered in time; using built-in lines");
        return new Dictionary<long, string>();
    }

    private async Task<Dictionary<long, string>> RequestLinesAsync(GameSession session, IReadOnlyList<GameEvent> events,
        string model, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(_settings.BaseUrl.EndsWith('/') ? _settings.BaseUrl : _settings.BaseUrl + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        var timeout = cancellationToken;

        var request = new ChatRequest(
            model,
            new[]
            {
                new ChatMessage("system", SystemPrompt),
                new ChatMessage("user", BuildUserPrompt(session, events))
            },
            Temperature: 0.9,
            ResponseFormat: new JsonResponseFormat("json_object"),
            ReasoningEffort: _settings.ReasoningEffortFor(model));

        using var response = await client.PostAsJsonAsync("chat/completions", request, JsonOptions, timeout);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(timeout);
            string summary = body.Replace('\n', ' ').Replace("  ", " ");
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode}: {summary[..Math.Min(summary.Length, 200)]}");
        }

        var chat = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, timeout);
        string content = chat?.Choices?.FirstOrDefault()?.Message?.Content
                         ?? throw new InvalidOperationException("Commentary API returned no content.");

        var parsed = JsonSerializer.Deserialize<CommentaryReply>(StripCodeFence(content), JsonOptions);
        var validSequences = events.Select(e => e.Sequence).ToHashSet();

        return (parsed?.Lines ?? new List<CommentaryLine>())
            .Where(l => validSequences.Contains(l.Seq) && !string.IsNullOrWhiteSpace(l.Text))
            .GroupBy(l => l.Seq)
            .ToDictionary(g => g.Key, g => g.First().Text!.Trim());
    }

    private static string BuildUserPrompt(GameSession session, IReadOnlyList<GameEvent> events)
    {
        var sb = new StringBuilder();
        var human = session.GetHumanPlayer();
        sb.AppendLine($"Round {session.Round}. The human player is \"{human.DisplayName}\"; everyone else is an NPC.");
        sb.AppendLine("Board now: " + string.Join("; ", session.Countries.Select(c =>
            $"{c.Name} ({session.GetPlayer(c.OwnerId).DisplayName}, {c.SoldierCount})")));
        sb.AppendLine();
        sb.AppendLine("Battle events, oldest first:");

        foreach (var e in events)
        {
            sb.Append($"seq {e.Sequence} [{e.Type}]: {e.Message}");
            if (e.AttackerDice.Count > 0)
                sb.Append($" Attacker dice {string.Join(",", e.AttackerDice)} vs defender dice {string.Join(",", e.DefenderDice)}.");
            if (e.WinOdds is double odds)
                sb.Append($" Attacker's odds of conquest now {odds:P0}.");
            if (!string.IsNullOrEmpty(e.Reasoning))
                sb.Append($" (NPC reasoning: {e.Reasoning})");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static (string Attacker, string Defender) Names(GameSession session, GameEvent e)
    {
        string attacker = e.ActorPlayerId is not null
            ? session.Players.FirstOrDefault(p => p.PlayerId == e.ActorPlayerId)?.DisplayName ?? "The attacker"
            : "The attacker";
        string defender = e.ToCountryCode is not null
            ? session.Countries.FirstOrDefault(c => c.Code == e.ToCountryCode)?.Name ?? "the defenders"
            : "the defenders";
        return (attacker, defender);
    }

    private static string StripCodeFence(string content)
    {
        string text = content.Trim();
        if (!text.StartsWith("```"))
            return text;

        int firstNewline = text.IndexOf('\n');
        int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline ? text[(firstNewline + 1)..lastFence].Trim() : text;
    }

    // ── OpenAI-compatible wire format ─────────────────────────────────────────

    private sealed record ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        double Temperature,
        [property: JsonPropertyName("response_format")] JsonResponseFormat ResponseFormat,
        [property: JsonPropertyName("reasoning_effort"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ReasoningEffort);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record JsonResponseFormat(string Type);

    private sealed record ChatResponse(List<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatMessage? Message);

    private sealed record CommentaryReply(List<CommentaryLine>? Lines);

    private sealed record CommentaryLine(long Seq, string? Text);
}
