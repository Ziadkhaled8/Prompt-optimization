namespace PromptOptimizer.Models;

/// <summary>
/// The result of running a prompt candidate on a single test case (possibly averaged over repeats).
/// </summary>
public sealed class RunResult
{
    /// <summary>Which candidate produced this result.</summary>
    public required string CandidateId { get; init; }

    /// <summary>Which test case was used.</summary>
    public required string TestCaseId { get; init; }

    /// <summary>The raw output(s) from the model. Multiple if repeats > 1.</summary>
    public required IReadOnlyList<string> Outputs { get; init; }

    /// <summary>Per-criterion scores (averaged over repeats if applicable).</summary>
    public required IReadOnlyList<CriterionScore> CriterionScores { get; init; }

    /// <summary>Weighted aggregate score (0.0–1.0). Zero if any hard-fail criterion failed.</summary>
    public required double AggregateScore { get; init; }

    /// <summary>Total token cost for this run (input + output tokens across all repeats).</summary>
    public long TokensUsed { get; init; }

    /// <summary>Estimated dollar cost for this run.</summary>
    public decimal EstimatedCost { get; init; }
}
