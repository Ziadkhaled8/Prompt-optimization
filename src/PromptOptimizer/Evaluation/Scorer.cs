using PromptOptimizer.Evaluation.Checks;
using PromptOptimizer.Models;

namespace PromptOptimizer.Evaluation;

/// <summary>
/// Computes weighted aggregate scores from individual criterion scores.
/// Applies hard-fail logic: if any hard-fail criterion fails, aggregate = 0.
/// </summary>
public sealed class Scorer
{
    private readonly Dictionary<string, IDeterministicCheck> _checks;

    public Scorer(IEnumerable<IDeterministicCheck> checks)
    {
        _checks = checks.ToDictionary(c => c.CheckType);
    }

    /// <summary>
    /// Runs all deterministic checks for a given output against the rubric's deterministic criteria.
    /// </summary>
    public IReadOnlyList<CriterionScore> RunDeterministicChecks(string output, Rubric rubric)
    {
        return rubric.Criteria
            .Where(c => c.Type == CriterionType.Deterministic)
            .Select(criterion =>
            {
                // Map criterion ID prefix to check type
                var checkType = ResolveCheckType(criterion);
                if (_checks.TryGetValue(checkType, out var check))
                {
                    return check.Evaluate(output, criterion);
                }

                return new CriterionScore
                {
                    CriterionId = criterion.Id,
                    Score = 0.0,
                    Passed = false,
                    Reason = $"No deterministic check registered for type '{checkType}'."
                };
            })
            .ToList();
    }

    /// <summary>
    /// Computes the weighted aggregate score from all criterion scores.
    /// Returns 0.0 if any hard-fail criterion failed.
    /// </summary>
    public double ComputeAggregate(IReadOnlyList<CriterionScore> scores, Rubric rubric)
    {
        var criterionMap = rubric.Criteria.ToDictionary(c => c.Id);

        // Check hard-fails first
        foreach (var score in scores)
        {
            if (criterionMap.TryGetValue(score.CriterionId, out var criterion)
                && criterion.HardFail
                && !score.Passed)
            {
                return 0.0;
            }
        }

        // Weighted average
        double totalWeight = 0;
        double weightedSum = 0;

        foreach (var score in scores)
        {
            if (criterionMap.TryGetValue(score.CriterionId, out var criterion))
            {
                totalWeight += criterion.Weight;
                weightedSum += score.Score * criterion.Weight;
            }
        }

        return totalWeight > 0 ? weightedSum / totalWeight : 0.0;
    }

    /// <summary>
    /// Resolves a criterion ID to its deterministic check type.
    /// Convention: criterion ID starts with the check type (e.g. "json-valid-output" → "json-valid").
    /// </summary>
    private static string ResolveCheckType(Criterion criterion)
    {
        // Check if the ID starts with a known check type prefix
        string[] knownTypes = ["json-valid", "max-length", "required-fields", "forbidden-phrase"];
        foreach (var t in knownTypes)
        {
            if (criterion.Id.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                return t;
        }

        // Fall back to full ID
        return criterion.Id;
    }
}
