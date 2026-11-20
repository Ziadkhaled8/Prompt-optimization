namespace PromptOptimizer.Models;

/// <summary>
/// The top-level experiment container. Holds all configuration and results for one optimization run.
/// </summary>
public sealed class Experiment
{
    /// <summary>Unique experiment identifier (e.g. "2026-11-20-support-classifier").</summary>
    public required string Id { get; init; }

    /// <summary>When the experiment was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The original prompt text.</summary>
    public required string OriginalPromptText { get; init; }

    /// <summary>Placeholder names found in the prompt.</summary>
    public required IReadOnlyList<string> Placeholders { get; init; }

    /// <summary>Suitability classification result.</summary>
    public SuitabilityVerdict? Suitability { get; set; }

    /// <summary>The approved rubric.</summary>
    public Rubric? Rubric { get; set; }

    /// <summary>All test cases (both optimization and held-out).</summary>
    public IReadOnlyList<TestCase> TestCases { get; set; } = [];

    /// <summary>All prompt candidates (round 0 = original).</summary>
    public List<PromptCandidate> Candidates { get; init; } = [];

    /// <summary>Per-round summaries.</summary>
    public List<RoundSummary> RoundSummaries { get; init; } = [];

    /// <summary>Maximum number of optimization rounds.</summary>
    public int MaxRounds { get; init; } = 5;

    /// <summary>Maximum dollar cost before stopping.</summary>
    public decimal BudgetCap { get; init; } = 5.00m;

    /// <summary>Target score to stop early.</summary>
    public double TargetScore { get; init; } = 0.95;

    /// <summary>Number of times to repeat each run for averaging.</summary>
    public int RepeatsPerRun { get; init; } = 2;

    /// <summary>Current experiment status.</summary>
    public ExperimentStatus Status { get; set; } = ExperimentStatus.Created;

    /// <summary>Held-out validation results (populated in stage G).</summary>
    public HeldOutResult? HeldOutResult { get; set; }

    /// <summary>Total cost so far.</summary>
    public decimal TotalCost { get; set; }
}

public enum ExperimentStatus
{
    Created,
    SuitabilityChecked,
    RubricApproved,
    TestCasesApproved,
    Optimizing,
    Validating,
    Complete,
    Failed
}

/// <summary>
/// Results of held-out validation (stage G).
/// </summary>
public sealed class HeldOutResult
{
    /// <summary>Original prompt's mean score on held-out set.</summary>
    public required double OriginalScore { get; init; }

    /// <summary>Best candidate's mean score on held-out set.</summary>
    public required double OptimizedScore { get; init; }

    /// <summary>Optimization-set improvement vs held-out improvement gap. Large = overfitting.</summary>
    public required double OverfitGap { get; init; }

    /// <summary>Individual run results on the held-out set for the original prompt.</summary>
    public required IReadOnlyList<RunResult> OriginalResults { get; init; }

    /// <summary>Individual run results on the held-out set for the best candidate.</summary>
    public required IReadOnlyList<RunResult> OptimizedResults { get; init; }
}
