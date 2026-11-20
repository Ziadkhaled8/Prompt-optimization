namespace PromptOptimizer.Models;

/// <summary>
/// A failure pattern observed across multiple test cases.
/// Grouped by the FailureAnalyzer to guide the rewriter.
/// </summary>
public sealed class FailurePattern
{
    /// <summary>Short label for the failure pattern (e.g. "ignores length limit on long inputs").</summary>
    public required string Label { get; init; }

    /// <summary>Detailed description of what goes wrong.</summary>
    public required string Description { get; init; }

    /// <summary>Ids of the criteria that this pattern violates.</summary>
    public required IReadOnlyList<string> AffectedCriterionIds { get; init; }

    /// <summary>Ids of test cases that exhibit this pattern.</summary>
    public required IReadOnlyList<string> ExampleTestCaseIds { get; init; }

    /// <summary>How many test cases exhibit this pattern.</summary>
    public required int Frequency { get; init; }
}
