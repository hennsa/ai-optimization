using System.Text;

namespace AIReviewDesk.Core;

public enum ReviewContextClass { Normal, Large, Unsupported }

/// <summary>Versioned product tier derived from two failed real prompts near 1.516M chars.</summary>
public static class ReviewContextCapability
{
    public const int LargePromptCharacterBoundary = 1_500_000;
    public const int MaximumPromptCharacterBound = 1_600_000;
    public const string BoundaryVersion = "large-context-2";
    public const string LargeSuiteVersion = "large-context-suite-2";
    public const string LargeTierLabel = "Large context v2 — up to 1,600,000 composed prompt characters";

    public static PromptSizeEvidence Measure(string exactPrompt)
    {
        ArgumentNullException.ThrowIfNull(exactPrompt);
        return new(exactPrompt.Length, Encoding.UTF8.GetByteCount(exactPrompt), Classify(exactPrompt.Length), BoundaryVersion);
    }

    public static ReviewContextClass Classify(int characters)
    {
        if (characters < 0) throw new ArgumentOutOfRangeException(nameof(characters));
        if (characters > MaximumPromptCharacterBound) return ReviewContextClass.Unsupported;
        return characters >= LargePromptCharacterBoundary ? ReviewContextClass.Large : ReviewContextClass.Normal;
    }
}

public sealed record PromptSizeEvidence(int CharacterCount, int Utf8ByteCount, ReviewContextClass ContextClass, string BoundaryVersion);

/// <summary>Compact, content-free evidence from a large-context synthetic probe suite.</summary>
public sealed record LargeContextCertificate
{
    public string CliVersion { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string ReasoningEffort { get; init; } = "";
    public string AuthorityContract { get; init; } = "";
    public string ToolContract { get; init; } = "";
    public string[] ExpectedTools { get; init; } = [];
    public string OutputContract { get; init; } = "";
    public string OutputEnvelopeId { get; init; } = "";
    public int OutputEnvelopeVersion { get; init; }
    public string SuiteVersion { get; init; } = "";
    public string BoundaryVersion { get; init; } = "";
    public int TestedPromptCharacters { get; init; }
    public int TestedPromptUtf8Bytes { get; init; }
    public long? ActualInputTokens { get; init; }
    public long? ActualOutputTokens { get; init; }
    public CopilotModelContext? ContextCapabilityAtTest { get; init; }
    public OutputEnvelopeObservation[] EnvelopeObservations { get; init; } = [];
    public CertificationProbe[] Probes { get; init; } = [];
    public ReviewUsage? Usage { get; init; }
    public CertificationStatus Status { get; init; }
    public DateTimeOffset TestedAt { get; init; }
    public string? FailureReason { get; init; }
}
