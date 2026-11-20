using PromptOptimizer.Evaluation.Checks;
using PromptOptimizer.Models;

namespace PromptOptimizer.Tests.Evaluation;

public class DeterministicCheckTests
{
    [Fact]
    public void JsonValidCheck_ValidJson_ReturnsPass()
    {
        var check = new JsonValidCheck();
        var criterion = new Criterion
        {
            Id = "json-valid",
            Description = "Must be valid JSON",
            Type = CriterionType.Deterministic
        };

        var result = check.Evaluate("{\"key\": \"value\", \"count\": 42}", criterion);

        Assert.True(result.Passed);
        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public void JsonValidCheck_InvalidJson_ReturnsFail()
    {
        var check = new JsonValidCheck();
        var criterion = new Criterion
        {
            Id = "json-valid",
            Description = "Must be valid JSON",
            Type = CriterionType.Deterministic
        };

        var result = check.Evaluate("not json at all {", criterion);

        Assert.False(result.Passed);
        Assert.Equal(0.0, result.Score);
    }

    [Fact]
    public void MaxLengthCheck_WithinLimit_ReturnsPass()
    {
        var check = new MaxLengthCheck();
        var criterion = new Criterion
        {
            Id = "max-length-50",
            Description = "Max 50 characters",
            Type = CriterionType.Deterministic,
            CheckConfig = "50"
        };

        var result = check.Evaluate("Short response", criterion);

        Assert.True(result.Passed);
        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public void MaxLengthCheck_ExceedsLimit_ReturnsPartialCredit()
    {
        var check = new MaxLengthCheck();
        var criterion = new Criterion
        {
            Id = "max-length-10",
            Description = "Max 10 characters",
            Type = CriterionType.Deterministic,
            CheckConfig = "10"
        };

        var result = check.Evaluate("123456789012345", criterion); // 15 chars, 5 over

        Assert.False(result.Passed);
        Assert.Equal(0.5, result.Score, 2);
    }

    [Fact]
    public void RequiredFieldsCheck_AllFound_ReturnsPass()
    {
        var check = new RequiredFieldsCheck();
        var criterion = new Criterion
        {
            Id = "required-fields",
            Description = "Must contain category and confidence",
            Type = CriterionType.Deterministic,
            CheckConfig = "category, confidence"
        };

        var result = check.Evaluate("Category: Billing, Confidence: High", criterion);

        Assert.True(result.Passed);
        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public void RequiredFieldsCheck_MissingField_ReturnsPartialScore()
    {
        var check = new RequiredFieldsCheck();
        var criterion = new Criterion
        {
            Id = "required-fields",
            Description = "Must contain category and confidence",
            Type = CriterionType.Deterministic,
            CheckConfig = "category, confidence"
        };

        var result = check.Evaluate("Category: Billing only", criterion);

        Assert.False(result.Passed);
        Assert.Equal(0.5, result.Score, 2);
    }

    [Fact]
    public void ForbiddenPhraseCheck_NoForbidden_ReturnsPass()
    {
        var check = new ForbiddenPhraseCheck();
        var criterion = new Criterion
        {
            Id = "forbidden-phrase",
            Description = "No hedging language",
            Type = CriterionType.Deterministic,
            CheckConfig = "as an ai, i think, maybe"
        };

        var result = check.Evaluate("The answer is 42.", criterion);

        Assert.True(result.Passed);
        Assert.Equal(1.0, result.Score);
    }

    [Fact]
    public void ForbiddenPhraseCheck_ContainsForbidden_ReturnsFail()
    {
        var check = new ForbiddenPhraseCheck();
        var criterion = new Criterion
        {
            Id = "forbidden-phrase",
            Description = "No hedging language",
            Type = CriterionType.Deterministic,
            CheckConfig = "as an ai, i think, maybe"
        };

        var result = check.Evaluate("As an AI, I think the answer is 42.", criterion);

        Assert.False(result.Passed);
        Assert.True(result.Score < 1.0);
    }
}
