using PromptOptimizer.Models;

namespace PromptOptimizer.Evaluation.Checks;

/// <summary>
/// Checks whether the output contains any forbidden phrases.
/// CheckConfig should contain comma-separated forbidden phrases.
/// </summary>
public sealed class ForbiddenPhraseCheck : IDeterministicCheck
{
    public string CheckType => "forbidden-phrase";

    public CriterionScore Evaluate(string output, Criterion criterion)
    {
        if (string.IsNullOrWhiteSpace(criterion.CheckConfig))
        {
            return new CriterionScore
            {
                CriterionId = criterion.Id,
                Score = 1.0,
                Passed = true,
                Reason = "No forbidden phrases specified — passes by default."
            };
        }

        var forbiddenPhrases = criterion.CheckConfig
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var found = forbiddenPhrases
            .Where(phrase => output.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (found.Count == 0)
        {
            return new CriterionScore
            {
                CriterionId = criterion.Id,
                Score = 1.0,
                Passed = true,
                Reason = $"None of {forbiddenPhrases.Length} forbidden phrases found."
            };
        }

        // Score degrades with each forbidden phrase found
        var score = Math.Max(0.0, 1.0 - (double)found.Count / forbiddenPhrases.Length);

        return new CriterionScore
        {
            CriterionId = criterion.Id,
            Score = score,
            Passed = false,
            Reason = $"Found {found.Count} forbidden phrase(s): {string.Join(", ", found.Select(f => $"\"{f}\""))}"
        };
    }
}
