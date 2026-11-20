namespace PromptOptimizer.Models;

/// <summary>
/// A prompt version being evaluated. Round 0 is the original user prompt.
/// </summary>
public sealed class PromptCandidate
{
    /// <summary>Unique identifier (e.g. "round-0", "round-1").</summary>
    public required string Id { get; init; }

    /// <summary>The full prompt text with placeholders.</summary>
    public required string Text { get; init; }

    /// <summary>Id of the parent candidate this was derived from, or null for the original.</summary>
    public string? ParentId { get; init; }

    /// <summary>What was changed and why, written by the rewriting model.</summary>
    public string? ChangeLog { get; init; }

    /// <summary>Optimization round number (0 = original).</summary>
    public required int Round { get; init; }
}
