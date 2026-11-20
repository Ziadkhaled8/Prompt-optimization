using PromptOptimizer.Evaluation;

namespace PromptOptimizer.Tests.Evaluation;

public class RunnerTests
{
    [Fact]
    public void SubstitutePlaceholders_ReplacesCaseInsensitively()
    {
        var template = "Classify this email from {{Customer_Name}}: {{Email_Body}}";
        var inputs = new Dictionary<string, string>
        {
            ["customer_name"] = "Alice",
            ["email_body"] = "I need a refund."
        };

        var substituted = Runner.SubstitutePlaceholders(template, inputs);

        Assert.Equal("Classify this email from Alice: I need a refund.", substituted);
    }

    [Fact]
    public void ExtractPlaceholders_FindsAllUniqueTokens()
    {
        var template = "Analyze {{text}}. Summarize {{text}} into {{category}} with {{Tone}}.";
        var placeholders = Runner.ExtractPlaceholders(template);

        Assert.Equal(3, placeholders.Count);
        Assert.Contains("text", placeholders);
        Assert.Contains("category", placeholders);
        Assert.Contains("Tone", placeholders);
    }
}
