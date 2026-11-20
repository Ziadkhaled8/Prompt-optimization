using PromptOptimizer.Models;
using PromptOptimizer.Optimization;

namespace PromptOptimizer.Tests.Optimization;

public class StoppingRulesTests
{
    private readonly StoppingRules _stoppingRules = new();

    [Fact]
    public void Evaluate_MaxRoundsReached_Stops()
    {
        var experiment = new Experiment
        {
            Id = "exp-1",
            OriginalPromptText = "test",
            Placeholders = Array.Empty<string>(),
            MaxRounds = 3
        };

        var summaries = new List<RoundSummary>
        {
            new() { Round = 1, CandidateId = "round-1", MeanScore = 0.5, CriterionMeans = Array.Empty<CriterionMean>(), RunResults = Array.Empty<RunResult>() },
            new() { Round = 2, CandidateId = "round-2", MeanScore = 0.6, CriterionMeans = Array.Empty<CriterionMean>(), RunResults = Array.Empty<RunResult>() },
            new() { Round = 3, CandidateId = "round-3", MeanScore = 0.7, CriterionMeans = Array.Empty<CriterionMean>(), RunResults = Array.Empty<RunResult>() }
        };

        var decision = _stoppingRules.Evaluate(experiment, summaries);

        Assert.True(decision.ShouldStop);
        Assert.Contains("Maximum rounds", decision.Reason);
    }

    [Fact]
    public void Evaluate_TargetScoreReached_Stops()
    {
        var experiment = new Experiment
        {
            Id = "exp-2",
            OriginalPromptText = "test",
            Placeholders = Array.Empty<string>(),
            TargetScore = 0.90,
            MaxRounds = 5
        };

        var summaries = new List<RoundSummary>
        {
            new() { Round = 1, CandidateId = "round-1", MeanScore = 0.92, CriterionMeans = Array.Empty<CriterionMean>(), RunResults = Array.Empty<RunResult>() }
        };

        var decision = _stoppingRules.Evaluate(experiment, summaries);

        Assert.True(decision.ShouldStop);
        Assert.Contains("Target score", decision.Reason);
    }

    [Fact]
    public void Evaluate_BudgetCapExceeded_Stops()
    {
        var experiment = new Experiment
        {
            Id = "exp-3",
            OriginalPromptText = "test",
            Placeholders = Array.Empty<string>(),
            BudgetCap = 2.00m,
            TotalCost = 2.50m
        };

        var summaries = new List<RoundSummary>
        {
            new() { Round = 1, CandidateId = "round-1", MeanScore = 0.6, CriterionMeans = Array.Empty<CriterionMean>(), RunResults = Array.Empty<RunResult>() }
        };

        var decision = _stoppingRules.Evaluate(experiment, summaries);

        Assert.True(decision.ShouldStop);
        Assert.Contains("Budget cap", decision.Reason);
    }

    [Fact]
    public void Evaluate_PlateauDetected_Stops()
    {
        var experiment = new Experiment
        {
            Id = "exp-4",
            OriginalPromptText = "test",
            Placeholders = Array.Empty<string>(),
            MaxRounds = 10,
            TargetScore = 0.99
        };

        var summaries = new List<RoundSummary>
        {
            new() { Round = 1, CandidateId = "round-1", MeanScore = 0.700, CriterionMeans = Array.Empty<CriterionMean>(), RunResults = Array.Empty<RunResult>() },
            new() { Round = 2, CandidateId = "round-2", MeanScore = 0.705, CriterionMeans = Array.Empty<CriterionMean>(), RunResults = Array.Empty<RunResult>() },
            new() { Round = 3, CandidateId = "round-3", MeanScore = 0.708, CriterionMeans = Array.Empty<CriterionMean>(), RunResults = Array.Empty<RunResult>() }
        };

        var decision = _stoppingRules.Evaluate(experiment, summaries);

        Assert.True(decision.ShouldStop);
        Assert.Contains("Plateau detected", decision.Reason);
    }
}
