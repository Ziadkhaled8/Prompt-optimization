using PromptOptimizer.Llm;
using PromptOptimizer.Models;

namespace PromptOptimizer.Intake;

/// <summary>
/// Stage A: Classifies whether a prompt's task is objectively checkable,
/// partly checkable, or subjective.
/// </summary>
public sealed class SuitabilityClassifier
{
    private readonly ProviderClient _client;

    public SuitabilityClassifier(ProviderClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Classifies the suitability of a prompt for optimization.
    /// </summary>
    public async Task<SuitabilityVerdict> ClassifyAsync(string promptText, CancellationToken ct = default)
    {
        var systemPrompt =
            """
            You classify prompts by how objectively their output quality can be measured.
            
            Categories:
            - "objective": The task has clear, verifiable success criteria (classification, extraction, formatting, structured data).
            - "partly_checkable": Some criteria are objectively verifiable, others require judgment (summarization with constraints, rewriting with rules).
            - "subjective": Quality is primarily about taste, creativity, or personal preference (creative writing, brainstorming).
            
            Respond with ONLY a JSON object:
            {
              "level": "objective" | "partly_checkable" | "subjective",
              "reason": "<one-sentence explanation>"
            }
            """;

        var response = await _client.ChatJsonAsync<SuitabilityResponse>(
            systemPrompt,
            $"Classify this prompt:\n\n{promptText}",
            ct: ct);

        var level = response.Level.ToLowerInvariant() switch
        {
            "objective" => SuitabilityLevel.Objective,
            "partly_checkable" => SuitabilityLevel.PartlyCheckable,
            "subjective" => SuitabilityLevel.Subjective,
            _ => SuitabilityLevel.PartlyCheckable
        };

        return new SuitabilityVerdict
        {
            Level = level,
            Reason = response.Reason
        };
    }

    private sealed class SuitabilityResponse
    {
        public required string Level { get; init; }
        public required string Reason { get; init; }
    }
}
