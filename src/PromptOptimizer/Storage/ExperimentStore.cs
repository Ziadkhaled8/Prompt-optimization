using System.Text.Json;
using PromptOptimizer.Models;

namespace PromptOptimizer.Storage;

/// <summary>
/// Persists and loads experiment data as JSON files in the experiments/ directory.
/// Each experiment gets its own folder following the structure in experementFolder.md.
/// </summary>
public sealed class ExperimentStore
{
    private readonly string _baseDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ExperimentStore(string baseDir = "experiments")
    {
        _baseDir = Path.GetFullPath(baseDir);
    }

    /// <summary>
    /// Gets the directory path for an experiment.
    /// </summary>
    public string GetExperimentDir(string experimentId) =>
        Path.Combine(_baseDir, experimentId);

    /// <summary>
    /// Saves the original prompt to the experiment folder.
    /// </summary>
    public async Task SaveOriginalPromptAsync(string experimentId, string promptText)
    {
        var dir = EnsureDir(experimentId);
        await File.WriteAllTextAsync(Path.Combine(dir, "prompt.original.md"), promptText);
    }

    /// <summary>
    /// Saves the approved rubric.
    /// </summary>
    public async Task SaveRubricAsync(string experimentId, Rubric rubric)
    {
        var dir = EnsureDir(experimentId);
        var json = JsonSerializer.Serialize(rubric, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(dir, "rubric.json"), json);
    }

    /// <summary>
    /// Saves test cases split by set membership.
    /// </summary>
    public async Task SaveTestCasesAsync(string experimentId, IReadOnlyList<TestCase> testCases)
    {
        var dir = EnsureDir(experimentId);

        var optimization = testCases.Where(tc => tc.Set == TestSetMembership.Optimization).ToList();
        var heldOut = testCases.Where(tc => tc.Set == TestSetMembership.HeldOut).ToList();

        await File.WriteAllTextAsync(
            Path.Combine(dir, "tests.optimization.json"),
            JsonSerializer.Serialize(optimization, JsonOptions));

        await File.WriteAllTextAsync(
            Path.Combine(dir, "tests.heldout.json"),
            JsonSerializer.Serialize(heldOut, JsonOptions));
    }

    /// <summary>
    /// Saves a prompt candidate (round-N.md in candidates/).
    /// </summary>
    public async Task SaveCandidateAsync(string experimentId, PromptCandidate candidate)
    {
        var dir = EnsureDir(experimentId, "candidates");

        var content = $"""
            # {candidate.Id}
            
            Parent: {candidate.ParentId ?? "none (original)"}
            Round: {candidate.Round}
            
            ## Change Log
            
            {candidate.ChangeLog ?? "Original prompt — no changes."}
            
            ## Prompt
            
            {candidate.Text}
            """;

        await File.WriteAllTextAsync(
            Path.Combine(dir, $"{candidate.Id}.md"), content);
    }

    /// <summary>
    /// Saves run results for a round.
    /// </summary>
    public async Task SaveRunResultsAsync(string experimentId, int round, IReadOnlyList<RunResult> results)
    {
        var dir = EnsureDir(experimentId, "runs");
        var json = JsonSerializer.Serialize(results, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(dir, $"round-{round}.json"), json);
    }

    /// <summary>
    /// Saves the full experiment state.
    /// </summary>
    public async Task SaveExperimentAsync(Experiment experiment)
    {
        var dir = EnsureDir(experiment.Id);
        var json = JsonSerializer.Serialize(experiment, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(dir, "experiment.json"), json);
    }

    /// <summary>
    /// Loads a full experiment from disk.
    /// </summary>
    public async Task<Experiment?> LoadExperimentAsync(string experimentId)
    {
        var path = Path.Combine(GetExperimentDir(experimentId), "experiment.json");
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Experiment>(json, JsonOptions);
    }

    /// <summary>
    /// Loads the rubric from disk.
    /// </summary>
    public async Task<Rubric?> LoadRubricAsync(string experimentId)
    {
        var path = Path.Combine(GetExperimentDir(experimentId), "rubric.json");
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Rubric>(json, JsonOptions);
    }

    /// <summary>
    /// Loads test cases from disk (both optimization and held-out sets).
    /// </summary>
    public async Task<IReadOnlyList<TestCase>?> LoadTestCasesAsync(string experimentId)
    {
        var dir = GetExperimentDir(experimentId);
        var optPath = Path.Combine(dir, "tests.optimization.json");
        var heldPath = Path.Combine(dir, "tests.heldout.json");

        if (!File.Exists(optPath) && !File.Exists(heldPath)) return null;

        var allCases = new List<TestCase>();

        if (File.Exists(optPath))
        {
            var json = await File.ReadAllTextAsync(optPath);
            var cases = JsonSerializer.Deserialize<List<TestCase>>(json, JsonOptions);
            if (cases is not null) allCases.AddRange(cases);
        }

        if (File.Exists(heldPath))
        {
            var json = await File.ReadAllTextAsync(heldPath);
            var cases = JsonSerializer.Deserialize<List<TestCase>>(json, JsonOptions);
            if (cases is not null) allCases.AddRange(cases);
        }

        return allCases;
    }

    /// <summary>
    /// Saves the HTML report.
    /// </summary>
    public async Task SaveReportAsync(string experimentId, string html)
    {
        var dir = EnsureDir(experimentId);
        await File.WriteAllTextAsync(Path.Combine(dir, "report.html"), html);
    }

    /// <summary>
    /// Lists all experiment IDs.
    /// </summary>
    public IReadOnlyList<string> ListExperiments()
    {
        if (!Directory.Exists(_baseDir)) return [];

        return Directory.GetDirectories(_baseDir)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .OrderDescending()
            .ToList();
    }

    private string EnsureDir(string experimentId, string? subDir = null)
    {
        var dir = GetExperimentDir(experimentId);
        if (subDir is not null)
            dir = Path.Combine(dir, subDir);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
