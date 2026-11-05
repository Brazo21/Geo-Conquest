namespace GeoConquest.Services.Commentary;

public sealed class CommentarySettings
{
    public const string SectionName = "Commentary";

    public bool Enabled { get; init; } = true;

    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta/openai/";

    public string Model { get; init; } = "gemini-3.8-flash";

    public string[] FallbackModels { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ModelsInOrder =>
        new[] { Model }.Concat(FallbackModels)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public string ApiKey { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 22;

    public int PerModelTimeoutSeconds { get; init; } = 10;

    public int MaxEventsPerRequest { get; init; } = 12;

    public string ReasoningEffort { get; init; } = "low";

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
