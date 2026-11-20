using PromptOptimizer.Llm;
using PromptOptimizer.Models;

namespace PromptOptimizer.Optimization;

/// <summary>
/// Stage E (part 1): Groups failures by pattern to give the rewriter actionable guidance.
/// </summary>
public sealed class FailureAnalyzer
{
    private readonly ProviderClient _client;

    public FailureAnalyzer(ProviderClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Analyzes run results to identify failure patterns.
    /// </summary>
    public async Task<IReadOnlyList<FailurePattern>> AnalyzeAsync(
        IReadOnlyList<RunResult> results,
        Rubric rubric,
        CancellationToken ct = default)
    {
        // Filter to failing cases
        var failures = results
            .Where(r => r.AggregateScore < 0.7 || r.CriterionScores.Any(s => !s.Passed))
            .ToList();

        if (failures.Count == 0)
            return [];

        var systemPrompt =
            """
            You analyze LLM output failures to find patterns. Group similar failures together
            so a prompt rewriter can make targeted fixes.
            
            For each pattern, identify:
            - A short label (e.g. "ignores length limit on long inputs")
            - A description of what goes wrong
            - Which criteria are affected
            - Which test cases exhibit the pattern
            
            Respond with ONLY a JSON object:
            {
              "patterns": [
                {
                  "label": "...",
                  "description": "...",
                  "affectedCriterionIds": ["..."],
                  "exampleTestCaseIds": ["..."],
                  "frequency": <number>
                }
              ]
            }
            """;

        var failureSummary = string.Join("\n\n", failures.Select(FormatFailure));

        var userMessage = $"""
            Task: {rubric.TaskDescription}
            
            Criteria:
            {string.Join("\n", rubric.Criteria.Select(c => $"  - [{c.Id}] {c.Description}"))}
            
            Failing cases:
            {failureSummary}
            """;

        var response = await _client.ChatJsonAsync<FailureAnalysisResponse>(
            systemPrompt, userMessage, ct: ct);

        return response.Patterns.Select(p => new FailurePattern
        {
            Label = p.Label,
            Description = p.Description,
            AffectedCriterionIds = p.AffectedCriterionIds,
            ExampleTestCaseIds = p.ExampleTestCaseIds,
            Frequency = p.Frequency
        }).ToList();
    }

    private static string FormatFailure(RunResult result)
    {
        var failedCriteria = result.CriterionScores
            .Where(s => !s.Passed)
            .Select(s => $"    - {s.CriterionId}: {s.Score:F2} — {s.Reason}");

        return $"""
            Test case: {result.TestCaseId} (aggregate: {result.AggregateScore:F2})
              Output excerpt: {Truncate(result.Outputs.FirstOrDefault() ?? "", 300)}
              Failed criteria:
            {string.Join("\n", failedCriteria)}
            """;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private sealed class FailureAnalysisResponse
    {
        public required List<FailurePatternDto> Patterns { get; init; }
    }

    private sealed class FailurePatternDto
    {
        public required string Label { get; init; }
        public required string Description { get; init; }
        public required List<string> AffectedCriterionIds { get; init; }
        public required List<string> ExampleTestCaseIds { get; init; }
        public int Frequency { get; init; }
    }
}
