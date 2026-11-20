namespace PromptOptimizer.Models;

/// <summary>
/// The complete rubric: a task description plus an ordered list of success criteria.
/// Created by the RubricDrafter, then approved/edited by the user.
/// </summary>
public sealed class Rubric
{
    /// <summary>One-sentence description of what the prompt is supposed to do.</summary>
    public required string TaskDescription { get; init; }

    /// <summary>Expected shape of the output (e.g. "JSON object", "plain paragraph", "bullet list").</summary>
    public required string ExpectedOutputShape { get; init; }

    /// <summary>Expected input types (e.g. "customer email text", "product name + category").</summary>
    public required string ExpectedInputTypes { get; init; }

    /// <summary>Ordered list of success criteria.</summary>
    public required IReadOnlyList<Criterion> Criteria { get; init; }

    /// <summary>Whether the user has approved this rubric.</summary>
    public bool Approved { get; set; }
}
