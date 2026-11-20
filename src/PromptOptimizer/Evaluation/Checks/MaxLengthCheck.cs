using PromptOptimizer.Models;

namespace PromptOptimizer.Evaluation.Checks;

/// <summary>
/// Checks whether the output is within a maximum character length.
/// CheckConfig should contain the max length as an integer string.
/// </summary>
public sealed class MaxLengthCheck : IDeterministicCheck
{
    public string CheckType => "max-length";

    public CriterionScore Evaluate(string output, Criterion criterion)
    {
        if (!int.TryParse(criterion.CheckConfig, out var maxLength))
        {
            return new CriterionScore
            {
                CriterionId = criterion.Id,
                Score = 0.0,
                Passed = false,
                Reason = $"Invalid CheckConfig for max-length: '{criterion.CheckConfig}'. Expected an integer."
            };
        }

        var actualLength = output.Length;
        if (actualLength <= maxLength)
        {
            return new CriterionScore
            {
                CriterionId = criterion.Id,
                Score = 1.0,
                Passed = true,
                Reason = $"Output length ({actualLength}) is within limit ({maxLength})."
            };
        }

        // Partial credit: how far over the limit
        var overBy = actualLength - maxLength;
        var score = Math.Max(0.0, 1.0 - (double)overBy / maxLength);

        return new CriterionScore
        {
            CriterionId = criterion.Id,
            Score = score,
            Passed = false,
            Reason = $"Output length ({actualLength}) exceeds limit ({maxLength}) by {overBy} characters."
        };
    }
}
