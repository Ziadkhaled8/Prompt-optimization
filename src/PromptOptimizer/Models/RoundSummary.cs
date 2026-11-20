namespace PromptOptimizer.Models;

/// <summary>
/// Aggregated scores for a single optimization round.
/// </summary>
public sealed class RoundSummary
{
    /// <summary>The round number.</summary>
    public required int Round { get; init; }

    /// <summary>The candidate evaluated in this round.</summary>
    public required string CandidateId { get; init; }

    /// <summary>Mean aggregate score across all optimization-set test cases.</summary>
    public required double MeanScore { get; init; }

    /// <summary>Per-criterion mean scores across all optimization-set test cases.</summary>
    public required IReadOnlyList<CriterionMean> CriterionMeans { get; init; }

    /// <summary>Individual run results for this round.</summary>
    public required IReadOnlyList<RunResult> RunResults { get; init; }

    /// <summary>Cumulative cost up to and including this round.</summary>
    public decimal CumulativeCost { get; init; }
}

/// <summary>
/// Mean score for a single criterion across all test cases in a round.
/// </summary>
public sealed class CriterionMean
{
    public required string CriterionId { get; init; }
    public required double MeanScore { get; init; }
    public required int PassCount { get; init; }
    public required int TotalCount { get; init; }
}
