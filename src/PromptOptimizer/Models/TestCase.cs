namespace PromptOptimizer.Models;

/// <summary>
/// A single test input for prompt evaluation.
/// </summary>
public sealed class TestCase
{
    /// <summary>Unique identifier within the experiment.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Input values keyed by placeholder name.
    /// E.g. { "email_text": "Hi, I want a refund…" }
    /// </summary>
    public required Dictionary<string, string> Inputs { get; init; }

    /// <summary>
    /// Optional expected-answer note (useful for classification/extraction tasks).
    /// Not a ground truth — used as a hint by the judge.
    /// </summary>
    public string? ExpectedNote { get; init; }

    /// <summary>Whether this case is in the optimization set or the held-out set.</summary>
    public required TestSetMembership Set { get; init; }

    /// <summary>Which category the test case was generated for (typical, edge, adversarial, etc.).</summary>
    public string? Category { get; init; }
}
