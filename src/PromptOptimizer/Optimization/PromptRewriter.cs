using PromptOptimizer.Llm;
using PromptOptimizer.Models;

namespace PromptOptimizer.Optimization;

/// <summary>
/// Stage E (part 2): Rewrites the prompt based on failure analysis.
/// Produces a targeted edit (not a full rewrite) with a change log.
/// </summary>
public sealed class PromptRewriter
{
    private readonly ProviderClient _client;

    public PromptRewriter(ProviderClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Rewrites the prompt to address identified failure patterns.
    /// </summary>
    public async Task<PromptCandidate> RewriteAsync(
        PromptCandidate currentCandidate,
        Rubric rubric,
        IReadOnlyList<FailurePattern> failures,
        IReadOnlyList<RunResult> failingResults,
        CancellationToken ct = default)
    {
        var systemPrompt =
            """
            You are a prompt engineering expert. You improve prompts by making small, targeted edits
            that address specific observed failures.
            
            Rules:
            - Make SMALL, TARGETED edits. Do not rewrite the entire prompt.
            - Each edit should address a specific failure pattern.
            - Preserve the prompt's intent and style.
            - Keep all placeholders ({{name}} format) intact.
            - Write a change log explaining which failure each edit targets.
            
            Respond with ONLY a JSON object:
            {
              "revisedPrompt": "...",
              "changeLog": "..."
            }
            """;

        var failuresSummary = string.Join("\n", failures.Select(f =>
            $"  - [{f.Label}] ({f.Frequency} cases): {f.Description}"));

        var exampleOutputs = string.Join("\n\n", failingResults.Take(5).Select(r =>
            $"Test {r.TestCaseId}:\n  Output: {Truncate(r.Outputs.FirstOrDefault() ?? "", 200)}\n  " +
            $"Failed: {string.Join(", ", r.CriterionScores.Where(s => !s.Passed).Select(s => $"{s.CriterionId}: {s.Reason}"))}"));

        var userMessage = $"""
            Current prompt (round {currentCandidate.Round}):
            {currentCandidate.Text}
            
            Rubric:
              Task: {rubric.TaskDescription}
              Output shape: {rubric.ExpectedOutputShape}
              Criteria: {string.Join(", ", rubric.Criteria.Select(c => $"{c.Id} ({c.Type})"))}
            
            Failure patterns:
            {failuresSummary}
            
            Example failing outputs:
            {exampleOutputs}
            """;

        var response = await _client.ChatJsonAsync<RewriteResponse>(
            systemPrompt, userMessage, ct: ct);

        return new PromptCandidate
        {
            Id = $"round-{currentCandidate.Round + 1}",
            Text = response.RevisedPrompt,
            ParentId = currentCandidate.Id,
            ChangeLog = response.ChangeLog,
            Round = currentCandidate.Round + 1
        };
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private sealed class RewriteResponse
    {
        public required string RevisedPrompt { get; init; }
        public required string ChangeLog { get; init; }
    }
}
