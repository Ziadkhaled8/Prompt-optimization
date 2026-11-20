using PromptOptimizer.Models;
using PromptOptimizer.TestGeneration;

namespace PromptOptimizer.Tests.TestGeneration;

public class DatasetSplitterTests
{
    [Fact]
    public void Split_PreservesTotalCount()
    {
        var splitter = new DatasetSplitter(seed: 42);
        var testCases = Enumerable.Range(1, 20).Select(i => new TestCase
        {
            Id = $"tc-{i:D2}",
            Inputs = new Dictionary<string, string> { ["text"] = $"input {i}" },
            Category = i % 2 == 0 ? "typical" : "short_long",
            Set = TestSetMembership.Optimization
        }).ToList();

        var split = splitter.Split(testCases, heldOutFraction: 0.2);

        Assert.Equal(20, split.Count);
        var heldOut = split.Where(tc => tc.Set == TestSetMembership.HeldOut).ToList();
        var optimization = split.Where(tc => tc.Set == TestSetMembership.Optimization).ToList();

        Assert.True(heldOut.Count >= 3);
        Assert.Equal(20, heldOut.Count + optimization.Count);
    }

    [Fact]
    public void Split_HeldOutSetHasCategoryRepresentation()
    {
        var splitter = new DatasetSplitter(seed: 42);
        var categories = new[] { "typical", "short_long", "ambiguous", "adversarial", "unusual_format" };
        var testCases = new List<TestCase>();

        int id = 1;
        foreach (var cat in categories)
        {
            for (int j = 0; j < 5; j++)
            {
                testCases.Add(new TestCase
                {
                    Id = $"tc-{id++:D2}",
                    Inputs = new Dictionary<string, string> { ["text"] = "test" },
                    Category = cat,
                    Set = TestSetMembership.Optimization
                });
            }
        }

        var split = splitter.Split(testCases, heldOutFraction: 0.2);
        var heldOut = split.Where(tc => tc.Set == TestSetMembership.HeldOut).ToList();

        // Must have at least 3 held-out items
        Assert.True(heldOut.Count >= 3);
        // Categories should be represented
        var heldOutCategories = heldOut.Select(c => c.Category).Distinct().ToList();
        Assert.True(heldOutCategories.Count >= 3);
    }
}
