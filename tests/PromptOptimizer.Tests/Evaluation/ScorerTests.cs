using PromptOptimizer.Evaluation;
using PromptOptimizer.Evaluation.Checks;
using PromptOptimizer.Models;

namespace PromptOptimizer.Tests.Evaluation;

public class ScorerTests
{
    private readonly Scorer _scorer;

    public ScorerTests()
    {
        _scorer = new Scorer(new IDeterministicCheck[]
        {
            new JsonValidCheck(),
            new MaxLengthCheck(),
            new RequiredFieldsCheck(),
            new ForbiddenPhraseCheck()
        });
    }

    [Fact]
    public void ComputeAggregate_WeightedScores_CalculatesCorrectAverage()
    {
        var rubric = new Rubric
        {
            TaskDescription = "Test Task",
            ExpectedInputTypes = "Text",
            ExpectedOutputShape = "JSON",
            Criteria = new[]
            {
                new Criterion { Id = "c1", Description = "Crit 1", Type = CriterionType.Deterministic, Weight = 2.0 },
                new Criterion { Id = "c2", Description = "Crit 2", Type = CriterionType.Judged, Weight = 1.0 }
            }
        };

        var scores = new[]
        {
            new CriterionScore { CriterionId = "c1", Score = 1.0, Passed = true, Reason = "Passed" },
            new CriterionScore { CriterionId = "c2", Score = 0.4, Passed = false, Reason = "Weak" }
        };

        // (1.0 * 2.0 + 0.4 * 1.0) / 3.0 = 2.4 / 3.0 = 0.8
        var aggregate = _scorer.ComputeAggregate(scores, rubric);

        Assert.Equal(0.8, aggregate, 3);
    }

    [Fact]
    public void ComputeAggregate_HardFailFailed_ReturnsZeroRegardlessOfOtherScores()
    {
        var rubric = new Rubric
        {
            TaskDescription = "Format check",
            ExpectedInputTypes = "Text",
            ExpectedOutputShape = "JSON",
            Criteria = new[]
            {
                new Criterion { Id = "json-valid", Description = "Must be JSON", Type = CriterionType.Deterministic, Weight = 1.0, HardFail = true },
                new Criterion { Id = "tone-polite", Description = "Must be polite", Type = CriterionType.Judged, Weight = 5.0, HardFail = false }
            }
        };

        var scores = new[]
        {
            new CriterionScore { CriterionId = "json-valid", Score = 0.0, Passed = false, Reason = "Invalid JSON" },
            new CriterionScore { CriterionId = "tone-polite", Score = 1.0, Passed = true, Reason = "Very polite" }
        };

        var aggregate = _scorer.ComputeAggregate(scores, rubric);

        Assert.Equal(0.0, aggregate);
    }
}
