using System.Text.RegularExpressions;
using PromptOptimizer.Llm;
using PromptOptimizer.Models;

namespace PromptOptimizer.Evaluation;

/// <summary>
/// Runs a prompt candidate against test cases: substitutes placeholders, calls the LLM,
/// repeats for averaging, then scores each output.
/// </summary>
public sealed partial class Runner
{
    private readonly ProviderClient _runnerClient;
    private readonly LlmJudge _judge;
    private readonly Scorer _scorer;

    public Runner(ProviderClient runnerClient, LlmJudge judge, Scorer scorer)
    {
        _runnerClient = runnerClient;
        _judge = judge;
        _scorer = scorer;
    }

    /// <summary>
    /// Runs a candidate prompt against a set of test cases and returns scored results.
    /// </summary>
    public async Task<IReadOnlyList<RunResult>> RunAndScoreAsync(
        PromptCandidate candidate,
        IReadOnlyList<TestCase> testCases,
        Rubric rubric,
        int repeats = 2,
        CancellationToken ct = default)
    {
        var results = new List<RunResult>();

        foreach (var testCase in testCases)
        {
            var result = await RunSingleTestCaseAsync(candidate, testCase, rubric, repeats, ct);
            results.Add(result);
        }

        return results;
    }

    private async Task<RunResult> RunSingleTestCaseAsync(
        PromptCandidate candidate,
        TestCase testCase,
        Rubric rubric,
        int repeats,
        CancellationToken ct)
    {
        var filledPrompt = SubstitutePlaceholders(candidate.Text, testCase.Inputs);

        // Run multiple times for averaging
        var outputs = new List<string>();
        var allCriterionScores = new List<IReadOnlyList<CriterionScore>>();
        long totalTokens = 0;
        decimal totalCost = 0;

        for (int i = 0; i < repeats; i++)
        {
            var response = await _runnerClient.ChatAsync(
                "You are a helpful assistant. Follow the instructions precisely.",
                filledPrompt, ct);

            outputs.Add(response.Text);
            totalTokens += response.InputTokens + response.OutputTokens;
            totalCost += response.EstimatedCost;

            // Score this output
            var deterministicScores = _scorer.RunDeterministicChecks(response.Text, rubric);
            var judgedScores = await _judge.JudgeAsync(
                candidate.Text, response.Text, rubric, testCase, ct);

            var combinedScores = deterministicScores.Concat(judgedScores).ToList();
            allCriterionScores.Add(combinedScores);
        }

        // Average scores across repeats
        var averagedScores = AverageScores(allCriterionScores);
        var aggregate = _scorer.ComputeAggregate(averagedScores, rubric);

        return new RunResult
        {
            CandidateId = candidate.Id,
            TestCaseId = testCase.Id,
            Outputs = outputs,
            CriterionScores = averagedScores,
            AggregateScore = aggregate,
            TokensUsed = totalTokens,
            EstimatedCost = totalCost
        };
    }

    /// <summary>
    /// Substitutes {{placeholder}} tokens in the prompt with actual values.
    /// </summary>
    public static string SubstitutePlaceholders(string promptText, Dictionary<string, string> inputs)
    {
        var result = promptText;
        foreach (var (key, value) in inputs)
        {
            result = result.Replace($"{{{{{key}}}}}", value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    /// <summary>
    /// Extracts placeholder names from a prompt text ({{name}} pattern).
    /// </summary>
    public static IReadOnlyList<string> ExtractPlaceholders(string promptText)
    {
        return PlaceholderRegex().Matches(promptText)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Averages criterion scores across multiple repeats.
    /// </summary>
    private static IReadOnlyList<CriterionScore> AverageScores(
        List<IReadOnlyList<CriterionScore>> allScores)
    {
        if (allScores.Count == 0) return [];
        if (allScores.Count == 1) return allScores[0];

        var grouped = allScores
            .SelectMany(scores => scores)
            .GroupBy(s => s.CriterionId);

        return grouped.Select(g =>
        {
            var avgScore = g.Average(s => s.Score);
            var passRate = g.Average(s => s.Passed ? 1.0 : 0.0);
            var reasons = string.Join(" | ", g.Select(s => s.Reason).Distinct());

            return new CriterionScore
            {
                CriterionId = g.Key,
                Score = avgScore,
                Passed = passRate >= 0.5, // majority vote
                Reason = reasons
            };
        }).ToList();
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex PlaceholderRegex();
}
