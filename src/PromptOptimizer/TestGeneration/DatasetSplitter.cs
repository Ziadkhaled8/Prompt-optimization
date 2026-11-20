using PromptOptimizer.Models;

namespace PromptOptimizer.TestGeneration;

/// <summary>
/// Splits test cases into optimization (~80%) and held-out (~20%) sets.
/// Ensures category diversity in both splits.
/// </summary>
public sealed class DatasetSplitter
{
    private readonly Random _rng;

    public DatasetSplitter(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Splits test cases into optimization and held-out sets.
    /// Returns a new list with updated Set membership.
    /// </summary>
    /// <param name="testCases">All generated test cases.</param>
    /// <param name="heldOutFraction">Fraction to hold out (default: ~0.2, min 3 cases).</param>
    public IReadOnlyList<TestCase> Split(
        IReadOnlyList<TestCase> testCases,
        double heldOutFraction = 0.2)
    {
        var heldOutCount = Math.Max(3, (int)Math.Ceiling(testCases.Count * heldOutFraction));
        heldOutCount = Math.Min(heldOutCount, testCases.Count - 1); // Keep at least 1 for optimization

        // Group by category, then pick proportionally from each
        var byCategory = testCases
            .GroupBy(tc => tc.Category ?? "unknown")
            .ToDictionary(g => g.Key, g => g.ToList());

        var heldOutIds = new HashSet<string>();

        foreach (var (category, cases) in byCategory)
        {
            var countFromCategory = Math.Max(1,
                (int)Math.Round((double)cases.Count / testCases.Count * heldOutCount));
            countFromCategory = Math.Min(countFromCategory, cases.Count - 1);

            // Shuffle and pick
            var shuffled = cases.OrderBy(_ => _rng.Next()).ToList();
            foreach (var tc in shuffled.Take(countFromCategory))
            {
                heldOutIds.Add(tc.Id);
            }
        }

        // If we don't have enough held-out, add more randomly
        if (heldOutIds.Count < 3)
        {
            var remaining = testCases
                .Where(tc => !heldOutIds.Contains(tc.Id))
                .OrderBy(_ => _rng.Next())
                .Take(3 - heldOutIds.Count);
            foreach (var tc in remaining)
            {
                heldOutIds.Add(tc.Id);
            }
        }

        // Reconstruct with updated set membership
        return testCases.Select(tc => new TestCase
        {
            Id = tc.Id,
            Inputs = tc.Inputs,
            ExpectedNote = tc.ExpectedNote,
            Category = tc.Category,
            Set = heldOutIds.Contains(tc.Id)
                ? TestSetMembership.HeldOut
                : TestSetMembership.Optimization
        }).ToList();
    }
}
