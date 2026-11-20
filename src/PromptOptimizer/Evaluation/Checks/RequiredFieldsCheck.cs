using PromptOptimizer.Models;

namespace PromptOptimizer.Evaluation.Checks;

/// <summary>
/// Checks whether the output contains all required fields/keywords.
/// CheckConfig should contain comma-separated field names or keywords.
/// </summary>
public sealed class RequiredFieldsCheck : IDeterministicCheck
{
    public string CheckType => "required-fields";

    public CriterionScore Evaluate(string output, Criterion criterion)
    {
        if (string.IsNullOrWhiteSpace(criterion.CheckConfig))
        {
            return new CriterionScore
            {
                CriterionId = criterion.Id,
                Score = 0.0,
                Passed = false,
                Reason = "No required fields specified in CheckConfig."
            };
        }

        var requiredFields = criterion.CheckConfig
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var missing = requiredFields
            .Where(field => !output.Contains(field, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count == 0)
        {
            return new CriterionScore
            {
                CriterionId = criterion.Id,
                Score = 1.0,
                Passed = true,
                Reason = $"All {requiredFields.Length} required fields found."
            };
        }

        var score = (double)(requiredFields.Length - missing.Count) / requiredFields.Length;

        return new CriterionScore
        {
            CriterionId = criterion.Id,
            Score = score,
            Passed = false,
            Reason = $"Missing {missing.Count}/{requiredFields.Length} required fields: {string.Join(", ", missing)}"
        };
    }
}
