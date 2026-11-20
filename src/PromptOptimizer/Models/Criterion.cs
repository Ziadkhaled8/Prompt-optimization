namespace PromptOptimizer.Models;

/// <summary>
/// A single success criterion in a rubric.
/// </summary>
public sealed class Criterion
{
    /// <summary>Unique identifier within the rubric (e.g. "format-json", "tone-professional").</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable description of what the criterion checks.</summary>
    public required string Description { get; init; }

    /// <summary>Whether this criterion is checked by code or by an LLM judge.</summary>
    public required CriterionType Type { get; init; }

    /// <summary>Relative weight (0.0–1.0) used when computing the aggregate score.</summary>
    public double Weight { get; init; } = 1.0;

    /// <summary>If true, any failure on this criterion zeros the entire score for the test case.</summary>
    public bool HardFail { get; init; }

    /// <summary>
    /// Optional configuration for deterministic checks.
    /// For MaxLengthCheck: the max character count.
    /// For RequiredFieldsCheck: comma-separated field names.
    /// For ForbiddenPhraseCheck: comma-separated phrases.
    /// For JsonValidCheck: not used.
    /// </summary>
    public string? CheckConfig { get; init; }

    public override string ToString() =>
        $"[{Type}] {Id}: {Description} (weight={Weight}, hardFail={HardFail})";
}
