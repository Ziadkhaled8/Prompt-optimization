namespace PromptOptimizer.Models;

/// <summary>
/// Result of the suitability classification (Stage A).
/// </summary>
public sealed class SuitabilityVerdict
{
    /// <summary>How objectively checkable the prompt's task is.</summary>
    public required SuitabilityLevel Level { get; init; }

    /// <summary>Short explanation for why this level was chosen.</summary>
    public required string Reason { get; init; }
}

public enum SuitabilityLevel
{
    /// <summary>The task has clear, objectively measurable success criteria.</summary>
    Objective,

    /// <summary>Some criteria are checkable, others require judgment.</summary>
    PartlyCheckable,

    /// <summary>Quality is primarily subjective/taste-based.</summary>
    Subjective
}
