using System.Text.Json;
using PromptOptimizer.Models;

namespace PromptOptimizer.Evaluation.Checks;

/// <summary>
/// Checks whether the output is valid JSON.
/// </summary>
public sealed class JsonValidCheck : IDeterministicCheck
{
    public string CheckType => "json-valid";

    public CriterionScore Evaluate(string output, Criterion criterion)
    {
        try
        {
            using var doc = JsonDocument.Parse(output.Trim());
            return new CriterionScore
            {
                CriterionId = criterion.Id,
                Score = 1.0,
                Passed = true,
                Reason = "Output is valid JSON."
            };
        }
        catch (JsonException ex)
        {
            return new CriterionScore
            {
                CriterionId = criterion.Id,
                Score = 0.0,
                Passed = false,
                Reason = $"Invalid JSON: {ex.Message}"
            };
        }
    }
}
