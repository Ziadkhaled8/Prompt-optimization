using PromptOptimizer.Evaluation;
using PromptOptimizer.Models;

namespace PromptOptimizer.Validation;

/// <summary>
/// Stage G: Scores the original and best candidate on the held-out set only.
/// Reports the gap between optimization-set improvement and held-out improvement.
/// </summary>
public sealed class HeldOutValidator
{
    private readonly Runner _runner;

    public HeldOutValidator(Runner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Validates by comparing original and best candidate on held-out test cases only.
    /// </summary>
    public async Task<HeldOutResult> ValidateAsync(
        Experiment experiment,
        PromptCandidate bestCandidate,
        CancellationToken ct = default)
    {
        var rubric = experiment.Rubric
            ?? throw new InvalidOperationException("Rubric must be set before validation.");

        // CRITICAL: Only use held-out test cases
        var heldOutCases = experiment.TestCases
            .Where(tc => tc.Set == TestSetMembership.HeldOut)
            .ToList();

        if (heldOutCases.Count == 0)
            throw new InvalidOperationException("No held-out test cases available for validation.");

        var original = experiment.Candidates.First(c => c.Round == 0);

        // Score both on held-out set
        var originalResults = await _runner.RunAndScoreAsync(
            original, heldOutCases, rubric, experiment.RepeatsPerRun, ct);

        var optimizedResults = await _runner.RunAndScoreAsync(
            bestCandidate, heldOutCases, rubric, experiment.RepeatsPerRun, ct);

        var originalScore = originalResults.Average(r => r.AggregateScore);
        var optimizedScore = optimizedResults.Average(r => r.AggregateScore);

        // Calculate overfit gap
        var optimizationSetImprovement = GetOptimizationSetImprovement(experiment);
        var heldOutImprovement = optimizedScore - originalScore;
        var overfitGap = optimizationSetImprovement - heldOutImprovement;

        return new HeldOutResult
        {
            OriginalScore = originalScore,
            OptimizedScore = optimizedScore,
            OverfitGap = overfitGap,
            OriginalResults = originalResults,
            OptimizedResults = optimizedResults
        };
    }

    private static double GetOptimizationSetImprovement(Experiment experiment)
    {
        if (experiment.RoundSummaries.Count < 2)
            return 0.0;

        var firstScore = experiment.RoundSummaries[0].MeanScore;
        var bestScore = experiment.RoundSummaries.Max(r => r.MeanScore);
        return bestScore - firstScore;
    }
}
