using System.Text.Json.Serialization;

namespace AIReviewDesk.Core;

public sealed class ReviewValidationException(string message) : InvalidOperationException(message);

public sealed record ReviewExecutionSettings(string ModelId = "auto", string ReasoningEffort = "auto")
{
    [JsonIgnore] public bool IsAutoModel => ModelId == "auto";
    [JsonIgnore] public string Display => $"Model: {CopilotModelPolicy.DisplayName(ModelId)}\nReasoning: {CopilotModelPolicy.EffortName(ReasoningEffort)}";
}

public sealed record CopilotModelChoice(string Id, string Name, IReadOnlyList<string> Efforts, CopilotModelBilling? Billing = null, CopilotModelContext? Context = null);

/// <summary>Optional limits advertised by the installed Copilot metadata protocol.</summary>
public sealed record CopilotModelContext(long? MaxPromptTokens, long? MaxOutputTokens, long? MaxContextWindowTokens, IReadOnlyList<string> SupportedContextTiers)
{
    public string Display => string.Join("\n", new[]
    {
        $"Maximum prompt tokens: {Format(MaxPromptTokens)}",
        $"Maximum output tokens: {Format(MaxOutputTokens)}",
        $"Context window: {Format(MaxContextWindowTokens)}",
        $"Supported context tiers: {(SupportedContextTiers.Count == 0 ? "Unavailable" : string.Join(", ", SupportedContextTiers))}"
    });
    private static string Format(long? value) => value is long count ? count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "Unavailable";
}

/// <summary>Current billing metadata advertised for a model by the Copilot metadata RPC.</summary>
public sealed record CopilotModelBilling(decimal? Multiplier, CopilotTokenPrices? TokenPrices)
{
    public string Display
    {
        get
        {
            var details = new List<string>();
            if (Multiplier is decimal multiplier) details.Add($"Billing multiplier: {multiplier.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture)}×");
            if (TokenPrices is { } prices) details.Add(prices.Display);
            return details.Count == 0 ? "Pricing unavailable" : string.Join(" · ", details);
        }
    }
}

/// <summary>Advertised AI-credit cost for a standard token billing batch.</summary>
public sealed record CopilotTokenPrices(decimal? InputPrice, decimal? OutputPrice, decimal? CacheReadPrice,
    decimal? CacheWritePrice, decimal? CacheWrite1hPrice, long? BatchSize, CopilotLongContextPrices? LongContext)
{
    public string Display
    {
        get
        {
            var unit = BatchSize is long count ? $"AI credits per {count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} tokens" : "AI credits per batch";
            var rates = new[]
            {
                Rate("input", InputPrice, unit), Rate("output", OutputPrice, unit), Rate("cache read", CacheReadPrice, unit),
                Rate("cache write", CacheWritePrice, unit), Rate("1h cache write", CacheWrite1hPrice, unit)
            }.Where(value => value != null).ToList();
            var result = rates.Count == 0 ? $"Pricing unavailable (billing unit: {unit})" : string.Join(", ", rates);
            if (LongContext is { } extended) result += $" · Long context: {extended.Display}";
            return result;
        }
    }

    private static string? Rate(string name, decimal? value, string unit) => value is decimal amount
        ? $"{name} {amount.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture)} {unit}" : null;
}

public sealed record CopilotLongContextPrices(decimal? InputPrice, decimal? OutputPrice, decimal? CacheReadPrice,
    decimal? CacheWritePrice, decimal? CacheWrite1hPrice)
{
    public string Display => string.Join(", ", new[]
    {
        Rate("input", InputPrice), Rate("output", OutputPrice), Rate("cache read", CacheReadPrice),
        Rate("cache write", CacheWritePrice), Rate("1h cache write", CacheWrite1hPrice)
    }.Where(value => value != null));
    private static string? Rate(string name, decimal? value) => value is decimal amount
        ? $"{name} {amount.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture)} AI credits/batch" : null;
}

/// <summary>Windows CLI 1.0.91 models verified against the exact production tool contract.</summary>
public static class CopilotModelPolicy
{
    public const string Version = "1.0.91";
    public static ReviewExecutionSettings Default { get; } = new("claude-sonnet-5.5", "high");
    public static CopilotModelChoice Auto { get; } = new("auto", "Auto", ["auto"]);
    public static IReadOnlyList<CopilotModelChoice> Verified => [Auto, .. CertificationContract.BundledModels
        .Where(c => c.Status == CertificationStatus.Certified && CertificationContract.InvalidReason(c, Version, CertificationContract.BundledTools) == null)
        .Select(c => new CopilotModelChoice(c.ModelId, c.DisplayName, c.ReasoningEfforts))];
    public const string AutoExplanation = "Auto passes no override and uses the CLI's normal default. Discovery does not imply trust. Only currently certified models and reasoning levels can review repositories.";
    public static string DisplayName(string id) => Verified.FirstOrDefault(m => m.Id == id)?.Name ?? id;
    public static string EffortName(string value) => value switch { "xhigh" => "Extra high", "max" => "Max", null or "" => "Unavailable", _ => char.ToUpperInvariant(value[0]) + value[1..] };
    public static IReadOnlyList<string> Efforts(string? model) => Verified.FirstOrDefault(m => m.Id == model)?.Efforts ?? ["auto"];
    public static void Validate(ReviewExecutionSettings settings, string? version, IReadOnlyList<CopilotModelChoice>? available = null)
    {
        if (version != Version) throw new ReviewValidationException("Model selection requires the verified Windows Copilot CLI 1.0.91 contract.");
        if (!(available ?? Verified).Any(m => m.Id == settings.ModelId))
            throw new ReviewValidationException("The requested model is unavailable under the verified review contract. Refresh Copilot settings and choose a current model.");
        if (!(available ?? Verified).Single(m => m.Id == settings.ModelId).Efforts.Contains(settings.ReasoningEffort))
            throw new ReviewValidationException("The selected model does not support that reasoning level. Choose an available level or Auto.");
    }
    public static ReviewExecutionSettings Adjust(ReviewExecutionSettings requested, IReadOnlyList<CopilotModelChoice> available, out string? message)
    {
        var model = available.FirstOrDefault(m => m.Id == requested.ModelId);
        var settings = model == null ? new ReviewExecutionSettings() : requested with
        { ReasoningEffort = model.Efforts.Contains(requested.ReasoningEffort) ? requested.ReasoningEffort : model.Efforts[0] };
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
