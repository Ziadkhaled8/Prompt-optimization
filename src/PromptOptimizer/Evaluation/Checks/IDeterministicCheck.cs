using PromptOptimizer.Models;

namespace PromptOptimizer.Evaluation.Checks;

/// <summary>
/// Interface for deterministic checks that can verify an output without an LLM.
/// </summary>
public interface IDeterministicCheck
{
    /// <summary>The criterion type name this check handles (e.g. "json-valid", "max-length").</summary>
    string CheckType { get; }

    /// <summary>
    /// Evaluates the output against the criterion.
    /// </summary>
    /// <param name="output">The model's raw output.</param>
    /// <param name="criterion">The criterion with optional CheckConfig.</param>
    /// <returns>A score result.</returns>
    CriterionScore Evaluate(string output, Criterion criterion);
}
