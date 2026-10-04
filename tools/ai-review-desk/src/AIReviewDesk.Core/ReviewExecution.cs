using System.Text.Json.Serialization;

namespace AIReviewDesk.Core;

public sealed class ReviewValidationException(string message) : InvalidOperationException(message);

public sealed record ReviewExecutionSettings(string ModelId = "auto", string ReasoningEffort = "auto")
{
    [JsonIgnore] public bool IsAutoModel => ModelId == "auto";
    [JsonIgnore] public string Display => $"Model: {CopilotModelPolicy.DisplayName(ModelId)}\nReasoning: {CopilotModelPolicy.EffortName(ReasoningEffort)}";
}

public sealed record CopilotModelChoice(string Id, string Name, IReadOnlyList<string> Efforts);

/// <summary>Windows CLI 1.0.91 models verified against the exact production tool contract.</summary>
public static class CopilotModelPolicy
{
    public const string Version = "1.0.91";
    public static ReviewExecutionSettings Default { get; } = new("claude-sonnet-5.5", "high");
    public static CopilotModelChoice Auto { get; } = new("auto", "Auto", ["auto"]);
    public static IReadOnlyList<CopilotModelChoice> Verified { get; } =
    [
        Auto,
        new("claude-sonnet-5", "Claude Sonnet 5", ["auto", "low", "medium", "high", "xhigh", "max"]),
        new("claude-sonnet-5.5", "Claude Sonnet 5.5", ["auto", "low", "medium", "high", "xhigh", "max"])
    ];
    public const string AutoExplanation = "Auto passes no override and uses the CLI's normal default. Explicit choices are verified for CLI 1.0.91 and this account; other models may not satisfy the review tool contract.";
    public static string DisplayName(string id) => Verified.FirstOrDefault(m => m.Id == id)?.Name ?? id;
    public static string EffortName(string value) => value switch { "xhigh" => "Extra high", "max" => "Max", null or "" => "Unavailable", _ => char.ToUpperInvariant(value[0]) + value[1..] };
    public static IReadOnlyList<string> Efforts(string? model) => Verified.FirstOrDefault(m => m.Id == model)?.Efforts ?? ["auto"];
    public static void Validate(ReviewExecutionSettings settings, string? version, IReadOnlyList<CopilotModelChoice>? available = null)
    {
        if (version != Version) throw new ReviewValidationException("Model selection requires the verified Windows Copilot CLI 1.0.91 contract.");
        if (!Verified.Any(m => m.Id == settings.ModelId) || (available != null && !available.Any(m => m.Id == settings.ModelId)))
            throw new ReviewValidationException("The requested model is unavailable under the verified review contract. Refresh Copilot settings and choose a current model.");
        if (!Efforts(settings.ModelId).Contains(settings.ReasoningEffort) || (available != null && !available.Single(m => m.Id == settings.ModelId).Efforts.Contains(settings.ReasoningEffort)))
            throw new ReviewValidationException("The selected model does not support that reasoning level. Choose an available level or Auto.");
    }
    public static ReviewExecutionSettings Adjust(ReviewExecutionSettings requested, IReadOnlyList<CopilotModelChoice> available, out string? message)
    {
        var model = available.FirstOrDefault(m => m.Id == requested.ModelId);
        var settings = model == null ? new ReviewExecutionSettings() : requested with
        { ReasoningEffort = model.Efforts.Contains(requested.ReasoningEffort) ? requested.ReasoningEffort : "auto" };
        message = settings == requested ? null : $"Execution settings adjusted: {DisplayName(requested.ModelId)} / {requested.ReasoningEffort} is unavailable; using {DisplayName(settings.ModelId)} / {settings.ReasoningEffort}.";
        return settings;
    }
}

public sealed record ModelTokenUsage(string Model, long? InputTokens, long? OutputTokens, long? CacheReadTokens, long? CacheWriteTokens, long? ReasoningTokens);
public sealed record ReviewUsage(IReadOnlyList<ModelTokenUsage> Models, decimal? NanoAiUnits, decimal? PremiumRequestCost)
{
    // CLI 1.0.91's responseLimitsNanoAiuToAiCredits uses 1e9 nano-AI units per credit.
    [JsonIgnore] public decimal? AiCredits => NanoAiUnits / 1_000_000_000m;
    [JsonIgnore] public string Display => string.Join("\n", new[]
    {
        AiCredits is decimal credits ? $"AI credits consumed: {credits:0.######}" : null,
        PremiumRequestCost is decimal cost ? $"Runtime premium-request cost: {cost:0.###}" : null
    }.Where(s => s != null).Concat(Models.Select(m => $"{m.Model}: " + string.Join(" · ", new[]
    {
        m.InputTokens is long input ? $"input {input:N0} (includes cache)" : null,
        m.OutputTokens is long output ? $"output {output:N0}" : null,
        m.CacheReadTokens is long read ? $"cache read {read:N0}" : null,
        m.CacheWriteTokens is long write ? $"cache write {write:N0}" : null,
        m.ReasoningTokens is long reasoning ? $"reasoning tokens {reasoning:N0}" : null
    }.Where(s => s != null)))));
}

public sealed record CopilotQuota(string Unit, decimal Entitlement, decimal Used, decimal RemainingPercentage, bool Unlimited, DateTimeOffset FetchedAt)
{
    public string Display => $"{Unit} · {(Unlimited ? "Unlimited allowance" : $"{RemainingPercentage:0.#}% remaining · {Used:0.###} of {Entitlement:0.###} used")}\nChecked {FetchedAt.LocalDateTime:g}. Reset date unavailable in CLI 1.0.91. Account allowance is separate from per-review usage.";
}
