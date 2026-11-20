using PromptOptimizer.Llm;
using PromptOptimizer.Models;

namespace PromptOptimizer.Intake;

/// <summary>
/// Stage B: Analyzes a prompt and drafts a rubric with success criteria.
/// Each criterion is tagged as deterministic or judged, with weights and hard-fail flags.
/// </summary>
public sealed class RubricDrafter
{
    private readonly ProviderClient _client;

    public RubricDrafter(ProviderClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Generates a draft rubric from the prompt text and suitability verdict.
    /// The rubric must be reviewed and approved by the user before proceeding.
    /// </summary>
    public async Task<Rubric> DraftAsync(
        string promptText,
        SuitabilityVerdict suitability,
        CancellationToken ct = default)
    {
        var systemPrompt =
            """
            You are an expert at analyzing LLM prompts and defining measurable success criteria.
            
            Given a prompt, produce a rubric with:
            1. A one-sentence task description
            2. The expected input types
            3. The expected output shape
            4. A list of 4–8 success criteria, each with:
               - id: short kebab-case identifier (e.g. "json-valid", "max-length-200", "tone-professional")
               - description: what the criterion checks
               - type: "deterministic" (checkable by code) or "judged" (needs an LLM)
               - weight: 0.0–1.0 relative importance
               - hardFail: true if failing this should zero the entire score
               - checkConfig: (only for deterministic) the config string
            
            Deterministic check types and their checkConfig format:
            - "json-valid-*": no config needed (leave null)
            - "max-length-*": integer max character count (e.g. "200")
            - "required-fields-*": comma-separated field names (e.g. "name,email,category")
            - "forbidden-phrase-*": comma-separated phrases (e.g. "I think,maybe,probably")
            
            Respond with ONLY a JSON object matching this format:
            {
              "taskDescription": "...",
              "expectedInputTypes": "...",
              "expectedOutputShape": "...",
              "criteria": [
                {
                  "id": "...",
                  "description": "...",
                  "type": "deterministic" | "judged",
                  "weight": 0.0-1.0,
                  "hardFail": true | false,
                  "checkConfig": "..." | null
                }
              ]
            }
            """;

        var userMessage = $"""
            Suitability: {suitability.Level} — {suitability.Reason}
            
            Prompt to analyze:
            {promptText}
            """;

        var response = await _client.ChatJsonAsync<RubricDraftResponse>(
            systemPrompt, userMessage, ct: ct);

        return new Rubric
        {
            TaskDescription = response.TaskDescription,
            ExpectedInputTypes = response.ExpectedInputTypes,
            ExpectedOutputShape = response.ExpectedOutputShape,
            Approved = false,
            Criteria = response.Criteria.Select(c => new Criterion
            {
                Id = c.Id,
                Description = c.Description,
                Type = c.Type.Equals("deterministic", StringComparison.OrdinalIgnoreCase)
                    ? CriterionType.Deterministic
                    : CriterionType.Judged,
                Weight = c.Weight,
                HardFail = c.HardFail,
                CheckConfig = c.CheckConfig
            }).ToList()
        };
    }

    // Internal DTOs for JSON deserialization
    private sealed class RubricDraftResponse
    {
        public required string TaskDescription { get; init; }
        public required string ExpectedInputTypes { get; init; }
        public required string ExpectedOutputShape { get; init; }
        public required List<CriterionDto> Criteria { get; init; }
    }

    private sealed class CriterionDto
    {
        public required string Id { get; init; }
        public required string Description { get; init; }
        public required string Type { get; init; }
        public double Weight { get; init; } = 1.0;
        public bool HardFail { get; init; }
        public string? CheckConfig { get; init; }
    }
}
