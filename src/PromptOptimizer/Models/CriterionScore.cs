namespace PromptOptimizer.Models;

/// <summary>
/// Score for a single criterion on a single test case output.
/// </summary>
public sealed class CriterionScore
{
    /// <summary>The criterion this score is for.</summary>
    public required string CriterionId { get; init; }

    /// <summary>Normalized score from 0.0 (complete failure) to 1.0 (perfect).</summary>
    public required double Score { get; init; }

    /// <summary>Whether this criterion passed (score above threshold or deterministic pass).</summary>
    public required bool Passed { get; init; }

    /// <summary>One-sentence reason (from the judge or deterministic check).</summary>
    public required string Reason { get; init; }
}
