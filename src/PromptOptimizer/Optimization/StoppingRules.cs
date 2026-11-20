using PromptOptimizer.Models;

namespace PromptOptimizer.Optimization;

/// <summary>
/// Stage F: Decides whether to continue optimizing or stop.
/// Checks: target score reached, plateau (no improvement for N rounds), budget exceeded, max rounds.
/// </summary>
public sealed class StoppingRules
{
    /// <summary>
    /// Evaluates whether the optimization loop should stop.
    /// </summary>
    public StopDecision Evaluate(
        Experiment experiment,
        IReadOnlyList<RoundSummary> roundSummaries)
    {
        if (roundSummaries.Count == 0)
            return StopDecision.Continue("No rounds completed yet.");

        var latestRound = roundSummaries[^1];
        var currentRound = latestRound.Round;

        // 1. Max rounds reached
        if (currentRound >= experiment.MaxRounds)
            return StopDecision.Stop($"Maximum rounds ({experiment.MaxRounds}) reached.");

        // 2. Target score reached
        if (latestRound.MeanScore >= experiment.TargetScore)
            return StopDecision.Stop(
                $"Target score ({experiment.TargetScore:F2}) reached: {latestRound.MeanScore:F2}");

        // 3. Budget exceeded
        if (experiment.TotalCost >= experiment.BudgetCap)
            return StopDecision.Stop(
                $"Budget cap (${experiment.BudgetCap:F2}) exceeded: ${experiment.TotalCost:F2}");

        // 4. Plateau: no improvement for 2 consecutive rounds
        if (roundSummaries.Count >= 3)
        {
            var last3 = roundSummaries.TakeLast(3).Select(r => r.MeanScore).ToList();
            var improvement1 = last3[1] - last3[0];
            var improvement2 = last3[2] - last3[1];

            if (improvement1 <= 0.01 && improvement2 <= 0.01)
                return StopDecision.Stop(
                    $"Plateau detected: no meaningful improvement for 2 rounds " +
                    $"({last3[0]:F3} → {last3[1]:F3} → {last3[2]:F3}).");
        }

        return StopDecision.Continue(
            $"Round {currentRound} score: {latestRound.MeanScore:F2}. Continuing.");
    }
}

/// <summary>
/// Result of evaluating stopping rules.
/// </summary>
public sealed class StopDecision
{
    public bool ShouldStop { get; private init; }
    public string Reason { get; private init; } = string.Empty;

    public static StopDecision Stop(string reason) => new() { ShouldStop = true, Reason = reason };
    public static StopDecision Continue(string reason) => new() { ShouldStop = false, Reason = reason };
}
