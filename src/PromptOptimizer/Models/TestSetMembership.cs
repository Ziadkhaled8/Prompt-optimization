namespace PromptOptimizer.Models;

/// <summary>
/// Which split a test case belongs to.
/// </summary>
public enum TestSetMembership
{
    /// <summary>Used during the optimization loop (stages D–F).</summary>
    Optimization,

    /// <summary>Held out — only used for final validation (stage G). Never exposed to the rewrite step.</summary>
    HeldOut
}
