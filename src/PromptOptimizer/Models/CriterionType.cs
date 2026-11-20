namespace PromptOptimizer.Models;

/// <summary>
/// Whether a rubric criterion is checked by code or by an LLM judge.
/// </summary>
public enum CriterionType
{
    /// <summary>Checkable by code: format validity, length limits, required fields, forbidden phrases.</summary>
    Deterministic,

    /// <summary>Requires a model-based judge: relevance, tone, accuracy, coherence.</summary>
    Judged
}
