namespace GeoConquest.Services.Commentary;

/// <summary>
/// "Commentary" section of appsettings.json. Works with any OpenAI-compatible chat-completions API.
/// Default: Google Gemini (free tier). Keep the ApiKey in User Secrets, never in appsettings.json.
/// </summary>
public sealed class CommentarySettings
{
    public const string SectionName = "Commentary";

    public bool Enabled { get; init; } = true;

    /// <summary>Base URL ending in "/", e.g. https://generativelanguage.googleapis.com/v1beta/openai/</summary>
    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta/openai/";

    public string Model { get; init; } = "gemini-3.8-flash";

    /// <summary>Tried in order when the main model is busy (503), rate-limited (429) or unavailable (404).</summary>
    public string[] FallbackModels { get; init; } = Array.Empty<string>();

    /// <summary>Main model first, then the fallbacks, without duplicates or blanks.</summary>
    public IReadOnlyList<string> ModelsInOrder =>
        new[] { Model }.Concat(FallbackModels)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Total time the game waits for the AI (across all models), then uses fallback lines.</summary>
    public int TimeoutSeconds { get; init; } = 22;

    /// <summary>Longest a single model gets before moving on to the next one.</summary>
    public int PerModelTimeoutSeconds { get; init; } = 10;

    /// <summary>Most battle events sent to the AI in one request (NPC turns can produce many).</summary>
    public int MaxEventsPerRequest { get; init; } = 12;

    /// <summary>
    /// Thinking level sent as "reasoning_effort". Gemini 3.x models can't turn thinking off, so "low" is
    /// the fastest. Gemini 2.5 Flash models use "none" (thinking off) automatically. Empty = don't send.
    /// </summary>
    public string ReasoningEffort { get; init; } = "low";

    /// <summary>The reasoning effort to send for a given model.</summary>
    public string? ReasoningEffortFor(string model)
    {
        if (string.IsNullOrWhiteSpace(ReasoningEffort))
            return null;

        bool isGemini25Flash = model.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase) &&
                               !model.Contains("pro", StringComparison.OrdinalIgnoreCase);
        return isGemini25Flash ? "none" : ReasoningEffort;
    }

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(BaseUrl);
}
