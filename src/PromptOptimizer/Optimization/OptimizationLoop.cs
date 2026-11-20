using PromptOptimizer.Evaluation;
using PromptOptimizer.Models;

namespace PromptOptimizer.Optimization;

/// <summary>
/// Stage D–F: The main optimization loop.
/// Runs the prompt, analyzes failures, rewrites, and checks stopping conditions.
/// Never sees or touches the held-out set.
/// </summary>
public sealed class OptimizationLoop
{
    private readonly Runner _runner;
    private readonly FailureAnalyzer _failureAnalyzer;
    private readonly PromptRewriter _rewriter;
    private readonly StoppingRules _stoppingRules;

    public OptimizationLoop(
        Runner runner,
        FailureAnalyzer failureAnalyzer,
        PromptRewriter rewriter,
        StoppingRules stoppingRules)
    {
        _runner = runner;
        _failureAnalyzer = failureAnalyzer;
        _rewriter = rewriter;
        _stoppingRules = stoppingRules;
    }

    /// <summary>
    /// Runs the full optimization loop. Returns the best candidate and all round summaries.
    /// Supports resuming from a checkpoint when RoundSummaries already has entries.
    /// </summary>
    public async Task<OptimizationResult> RunAsync(
        Experiment experiment,
        IProgress<string>? progress = null,
        Func<Experiment, Task>? onRoundComplete = null,
        CancellationToken ct = default)
    {
        // CRITICAL: Only use optimization-set test cases. Never touch held-out.
        var optimizationCases = experiment.TestCases
            .Where(tc => tc.Set == TestSetMembership.Optimization)
            .ToList();

        var rubric = experiment.Rubric
            ?? throw new InvalidOperationException("Rubric must be set before optimization.");

        int startRound;
        PromptCandidate currentCandidate;
        PromptCandidate bestCandidate;
        double bestScore;
        IReadOnlyList<RunResult> results;

        if (experiment.RoundSummaries.Count > 0)
        {
            // ── Resume from checkpoint ──────────────────────────────
            var lastSummary = experiment.RoundSummaries[^1];
            startRound = lastSummary.Round + 1;

            bestScore = experiment.RoundSummaries.Max(r => r.MeanScore);
            var bestSummaryId = experiment.RoundSummaries
                .First(r => r.MeanScore == bestScore).CandidateId;
            bestCandidate = experiment.Candidates.First(c => c.Id == bestSummaryId);

            currentCandidate = experiment.Candidates
                .OrderByDescending(c => c.Round).First();
            results = lastSummary.RunResults;

            progress?.Report(
                $"Resuming from round {startRound} (best score so far: {bestScore:F3})");
        }
        else
        {
            // ── Fresh start: score round 0 ──────────────────────────
            currentCandidate = experiment.Candidates.First(c => c.Round == 0);
            progress?.Report("Round 0: Scoring original prompt...");

            results = await _runner.RunAndScoreAsync(
                currentCandidate, optimizationCases, rubric, experiment.RepeatsPerRun, ct);

            var roundSummary = BuildRoundSummary(0, currentCandidate.Id, results);
            experiment.RoundSummaries.Add(roundSummary);
            experiment.TotalCost += roundSummary.CumulativeCost;

            progress?.Report($"Round 0: Mean score = {roundSummary.MeanScore:F3}");

            bestCandidate = currentCandidate;
            bestScore = roundSummary.MeanScore;
            startRound = 1;

            if (onRoundComplete is not null) await onRoundComplete(experiment);
        }

        // Optimization rounds
        for (int round = startRound; round <= experiment.MaxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            // Check stopping rules
            var decision = _stoppingRules.Evaluate(experiment, experiment.RoundSummaries);
            if (decision.ShouldStop)
            {
                progress?.Report($"Stopping: {decision.Reason}");
                break;
            }

            progress?.Report($"Round {round}: Analyzing failures...");

            // Analyze failures
            var failingResults = results.Where(r => r.AggregateScore < 0.7).ToList();
            var patterns = await _failureAnalyzer.AnalyzeAsync(results, rubric, ct);

            if (patterns.Count == 0)
            {
                progress?.Report($"Round {round}: No failure patterns found. Stopping.");
                break;
            }

            // Rewrite
            progress?.Report($"Round {round}: Rewriting prompt ({patterns.Count} patterns found)...");
            var newCandidate = await _rewriter.RewriteAsync(
                currentCandidate, rubric, patterns, failingResults, ct);

            experiment.Candidates.Add(newCandidate);

            // Score the new candidate
            progress?.Report($"Round {round}: Scoring revised prompt...");
            results = await _runner.RunAndScoreAsync(
                newCandidate, optimizationCases, rubric, experiment.RepeatsPerRun, ct);

            var roundSummary = BuildRoundSummary(round, newCandidate.Id, results);
            experiment.RoundSummaries.Add(roundSummary);
            experiment.TotalCost += results.Sum(r => r.EstimatedCost);

            // Save checkpoint immediately after scoring
            if (onRoundComplete is not null) await onRoundComplete(experiment);

            progress?.Report($"Round {round}: Mean score = {roundSummary.MeanScore:F3} " +
                           $"(Δ = {roundSummary.MeanScore - bestScore:+0.000;-0.000})");

            // Track best
            if (roundSummary.MeanScore > bestScore)
            {
                bestScore = roundSummary.MeanScore;
                bestCandidate = newCandidate;
            }

            currentCandidate = newCandidate;
        }

        return new OptimizationResult
        {
            BestCandidate = bestCandidate,
            BestScore = bestScore,
            TotalRounds = experiment.RoundSummaries.Count,
            TotalCost = experiment.TotalCost
        };
    }

    private static RoundSummary BuildRoundSummary(int round, string candidateId, IReadOnlyList<RunResult> results)
    {
        var meanScore = results.Count > 0 ? results.Average(r => r.AggregateScore) : 0.0;

        var criterionMeans = results
            .SelectMany(r => r.CriterionScores)
            .GroupBy(s => s.CriterionId)
            .Select(g => new CriterionMean
            {
                CriterionId = g.Key,
                MeanScore = g.Average(s => s.Score),
                PassCount = g.Count(s => s.Passed),
                TotalCount = g.Count()
            })
            .ToList();

        return new RoundSummary
        {
            Round = round,
            CandidateId = candidateId,
            MeanScore = meanScore,
            CriterionMeans = criterionMeans,
            RunResults = results,
            CumulativeCost = results.Sum(r => r.EstimatedCost)
        };
    }
}

/// <summary>
/// Result of the optimization loop.
/// </summary>
public sealed class OptimizationResult
{
    public required PromptCandidate BestCandidate { get; init; }
    public required double BestScore { get; init; }
    public required int TotalRounds { get; init; }
    public required decimal TotalCost { get; init; }
}
