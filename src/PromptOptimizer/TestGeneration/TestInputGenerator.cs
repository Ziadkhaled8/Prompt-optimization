using PromptOptimizer.Llm;
using PromptOptimizer.Models;

namespace PromptOptimizer.TestGeneration;

/// <summary>
/// Stage C: Generates realistic test inputs for the prompt's placeholders.
/// Covers five categories: typical, short/long, ambiguous, adversarial, unusual formatting.
/// </summary>
public sealed class TestInputGenerator
{
    private readonly ProviderClient _client;

    public TestInputGenerator(ProviderClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Generates roughly 25 test inputs covering diverse categories.
    /// </summary>
    public async Task<IReadOnlyList<TestCase>> GenerateAsync(
        string promptText,
        Rubric rubric,
        IReadOnlyList<string> placeholders,
        int targetCount = 25,
        CancellationToken ct = default)
    {
        var systemPrompt =
            $$"""
            You generate realistic test inputs for LLM prompt evaluation.
            
            Generate exactly {{targetCount}} test inputs, distributed across these categories:
            1. **typical** (~8): Normal, representative inputs
            2. **short_long** (~5): Very short and very long inputs  
            3. **ambiguous** (~4): Inputs with missing info or unclear intent
            4. **adversarial** (~4): Inputs that try to derail the prompt (injection, off-topic, edge cases)
            5. **unusual_format** (~4): Unusual formatting, special characters, mixed languages
            
            Each test input should provide values for these placeholders: {{string.Join(", ", placeholders)}}
            
            For classification/extraction tasks, include an expected answer note.
            
            Respond with ONLY a JSON object:
            {
              "testCases": [
                {
                  "id": "tc-01",
                  "inputs": { "<placeholder>": "<value>", ... },
                  "expectedNote": "<optional expected answer hint>" | null,
                  "category": "typical" | "short_long" | "ambiguous" | "adversarial" | "unusual_format"
                }
              ]
            }
            """;

        var userMessage = $"""
            Task: {rubric.TaskDescription}
            Expected input types: {rubric.ExpectedInputTypes}
            Expected output shape: {rubric.ExpectedOutputShape}
            Placeholders: {string.Join(", ", placeholders)}
            
            Prompt:
            {promptText}
            """;

        var response = await _client.ChatJsonAsync<TestGenResponse>(
            systemPrompt, userMessage, ct: ct);

        return response.TestCases.Select(tc => new TestCase
        {
            Id = tc.Id,
            Inputs = new Dictionary<string, string>(tc.Inputs),
            ExpectedNote = tc.ExpectedNote,
            Category = tc.Category,
            Set = TestSetMembership.Optimization // Will be split later by DatasetSplitter
        }).ToList();
    }

    private sealed class TestGenResponse
    {
        public required List<TestCaseDto> TestCases { get; init; }
    }

    private sealed class TestCaseDto
    {
        public required string Id { get; init; }
        public required Dictionary<string, string> Inputs { get; init; }
        public string? ExpectedNote { get; init; }
        public string? Category { get; init; }
    }
}
