using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptOptimizer.Evaluation;
using PromptOptimizer.Evaluation.Checks;
using PromptOptimizer.Intake;
using PromptOptimizer.Llm;
using PromptOptimizer.Models;
using PromptOptimizer.Optimization;
using PromptOptimizer.Reporting;
using PromptOptimizer.Storage;
using PromptOptimizer.TestGeneration;
using PromptOptimizer.Validation;

// ─── Configuration ───────────────────────────────────────────────────────────

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("PROMPTOPT_")
    .Build();

// ─── Root command ────────────────────────────────────────────────────────────

var rootCommand = new RootCommand("PromptOptimizer — Measured, transparent prompt improvement");

// Global options
var providerOption = new Option<string?>(
    "--provider",
    "LLM provider strategy to use: 'Gemini' (Google free tier), 'Anthropic', 'GenericRest' (OpenAI/Ollama/Groq), or 'Mock'");

rootCommand.AddGlobalOption(providerOption);

// ─── optimize command ────────────────────────────────────────────────────────

var optimizeCommand = new Command("optimize", "Run the full optimization pipeline on a prompt");
var promptFileArg = new Argument<FileInfo>("prompt-file", "Path to a file containing the prompt to optimize");
var nameOption = new Option<string?>("--name", "Experiment name (auto-generated if omitted)");
var roundsOption = new Option<int>("--max-rounds", () => 2, "Maximum optimization rounds");
var budgetOption = new Option<decimal>("--budget", () => 5.00m, "Maximum dollar cost");
var repeatsOption = new Option<int>("--repeats", () => 1, "Repeats per test case for averaging");
var resumeOption = new Option<bool>("--resume", () => false, "Resume from the last saved checkpoint if experiment exists");

optimizeCommand.AddArgument(promptFileArg);
optimizeCommand.AddOption(nameOption);
optimizeCommand.AddOption(roundsOption);
optimizeCommand.AddOption(budgetOption);
optimizeCommand.AddOption(repeatsOption);
optimizeCommand.AddOption(resumeOption);

optimizeCommand.Handler = CommandHandler.Create<FileInfo, string?, int, decimal, int, bool, string?>(
    async (promptFile, name, maxRounds, budget, repeats, resume, provider) =>
{
    if (!promptFile.Exists)
    {
        Console.Error.WriteLine($"❌ File not found: {promptFile.FullName}");
        return 1;
    }

    var promptText = await File.ReadAllTextAsync(promptFile.FullName);
    var experimentId = name ?? $"{DateTime.Now:yyyy-MM-dd}-{Path.GetFileNameWithoutExtension(promptFile.Name)}";

    // Build services with strategy
    var services = BuildServices(config, provider);
    var store = services.GetRequiredService<ExperimentStore>();

    Experiment experiment;

    if (resume)
    {
        var existing = await store.LoadExperimentAsync(experimentId);
        if (existing is not null)
        {
            experiment = existing;
            Console.WriteLine($"🔄 Resuming experiment: {experimentId} (last status: {experiment.Status})");
            if (experiment.OriginalPromptText != promptText)
            {
                Console.WriteLine("   ⚠️  Warning: File prompt differs from saved prompt. Using saved experiment prompt.");
            }
        }
        else
        {
            Console.WriteLine($"⚠️  No checkpoint found for '{experimentId}'. Starting a new experiment.");
            var placeholders = Runner.ExtractPlaceholders(promptText);
            experiment = new Experiment
            {
                Id = experimentId,
                OriginalPromptText = promptText,
                Placeholders = placeholders,
                MaxRounds = maxRounds,
                BudgetCap = budget,
                RepeatsPerRun = repeats
            };
        }
    }
    else
    {
        if (File.Exists(Path.Combine(store.GetExperimentDir(experimentId), "experiment.json")))
        {
            Console.Error.WriteLine($"❌ Experiment '{experimentId}' already exists. Use --resume to continue from checkpoint.");
            return 1;
        }

        var placeholders = Runner.ExtractPlaceholders(promptText);
        Console.WriteLine($"🚀 Starting experiment: {experimentId}");
        Console.WriteLine($"   Rounds: {maxRounds} | Budget: ${budget:F2} | Repeats: {repeats}");
        Console.WriteLine($"📎 Found placeholders: {(placeholders.Count > 0 ? string.Join(", ", placeholders) : "(none)")}");
        Console.WriteLine();

        experiment = new Experiment
        {
            Id = experimentId,
            OriginalPromptText = promptText,
            Placeholders = placeholders,
            MaxRounds = maxRounds,
            BudgetCap = budget,
            RepeatsPerRun = repeats
        };
    }

    // ── Stage A: Suitability ────────────────────────────────────
    if (experiment.Suitability is not null && experiment.Status >= ExperimentStatus.SuitabilityChecked)
    {
        Console.WriteLine("\n── Stage A: Suitability Check (Loaded from checkpoint) ──");
        Console.WriteLine($"   {experiment.Suitability.Level}: {experiment.Suitability.Reason}");
    }
    else
    {
        Console.WriteLine("\n── Stage A: Suitability Check ──");
        var classifier = services.GetRequiredService<SuitabilityClassifier>();
        experiment.Suitability = await classifier.ClassifyAsync(experiment.OriginalPromptText);
        Console.WriteLine($"   {experiment.Suitability.Level}: {experiment.Suitability.Reason}");

        if (experiment.Suitability.Level == SuitabilityLevel.Subjective)
        {
            Console.Write("   ⚠️  This prompt is subjective. Continue with reduced confidence? (y/n): ");
            if (Console.ReadLine()?.Trim().ToLower() != "y")
            {
                Console.WriteLine("   Aborted.");
                return 0;
            }
        }

        experiment.Status = ExperimentStatus.SuitabilityChecked;
        await store.SaveOriginalPromptAsync(experimentId, experiment.OriginalPromptText);
        await store.SaveExperimentAsync(experiment);
    }

    // ── Stage B: Rubric ─────────────────────────────────────────
    Rubric rubric;
    if (experiment.Rubric is not null && experiment.Status >= ExperimentStatus.RubricApproved)
    {
        Console.WriteLine("\n── Stage B: Rubric Draft (Loaded from checkpoint) ──");
        rubric = experiment.Rubric;
        Console.WriteLine($"   Task: {rubric.TaskDescription}");
        Console.WriteLine($"   Output shape: {rubric.ExpectedOutputShape}");
    }
    else
    {
        Console.WriteLine("\n── Stage B: Rubric Draft ──");
        var drafter = services.GetRequiredService<RubricDrafter>();
        rubric = await drafter.DraftAsync(experiment.OriginalPromptText, experiment.Suitability!);

        Console.WriteLine($"   Task: {rubric.TaskDescription}");
        Console.WriteLine($"   Output shape: {rubric.ExpectedOutputShape}");
        Console.WriteLine("   Criteria:");
        foreach (var c in rubric.Criteria)
            Console.WriteLine($"     [{c.Type}] {c.Id}: {c.Description} (w={c.Weight}, hf={c.HardFail})");

        Console.Write("\n   Approve rubric? (y/n): ");
        if (Console.ReadLine()?.Trim().ToLower() != "y")
        {
            Console.WriteLine("   Please edit the rubric in rubric.json and re-run with --resume.");
            await store.SaveRubricAsync(experimentId, rubric);
            await store.SaveExperimentAsync(experiment);
            return 0;
        }

        rubric.Approved = true;
        experiment.Rubric = rubric;
        experiment.Status = ExperimentStatus.RubricApproved;
        await store.SaveRubricAsync(experimentId, rubric);
        await store.SaveExperimentAsync(experiment);
    }

    // ── Stage C: Test Generation ────────────────────────────────
    IReadOnlyList<TestCase> splitCases;
    if (experiment.TestCases.Count > 0 && experiment.Status >= ExperimentStatus.TestCasesApproved)
    {
        Console.WriteLine("\n── Stage C: Test Input Generation (Loaded from checkpoint) ──");
        splitCases = experiment.TestCases;
        var optCount = splitCases.Count(tc => tc.Set == TestSetMembership.Optimization);
        var heldOutCount = splitCases.Count(tc => tc.Set == TestSetMembership.HeldOut);
        Console.WriteLine($"   Loaded {splitCases.Count} test cases ({optCount} optimization, {heldOutCount} held-out)");
    }
    else
    {
        Console.WriteLine("\n── Stage C: Test Input Generation ──");
        var generator = services.GetRequiredService<TestInputGenerator>();
        var testCases = await generator.GenerateAsync(experiment.OriginalPromptText, rubric, experiment.Placeholders);
        Console.WriteLine($"   Generated {testCases.Count} test cases");

        var splitter = services.GetRequiredService<DatasetSplitter>();
        splitCases = splitter.Split(testCases);
        var optCount = splitCases.Count(tc => tc.Set == TestSetMembership.Optimization);
        var heldOutCount = splitCases.Count(tc => tc.Set == TestSetMembership.HeldOut);
        Console.WriteLine($"   Split: {optCount} optimization, {heldOutCount} held-out");

        Console.Write("   Approve test cases? (y/n): ");
        if (Console.ReadLine()?.Trim().ToLower() != "y")
        {
            Console.WriteLine("   Please review test cases and re-run with --resume.");
            await store.SaveTestCasesAsync(experimentId, splitCases);
            await store.SaveExperimentAsync(experiment);
            return 0;
        }

        experiment.TestCases = splitCases;
        experiment.Status = ExperimentStatus.TestCasesApproved;
        await store.SaveTestCasesAsync(experimentId, splitCases);
        await store.SaveExperimentAsync(experiment);
    }

    // ── Stage D–F: Optimization Loop ────────────────────────────
    Console.WriteLine("\n── Stages D–F: Optimization ──");
    if (!experiment.Candidates.Any(c => c.Round == 0))
    {
        var round0Candidate = new PromptCandidate
        {
            Id = "round-0",
            Text = experiment.OriginalPromptText,
            Round = 0
        };
        experiment.Candidates.Add(round0Candidate);
        await store.SaveCandidateAsync(experimentId, round0Candidate);
    }

    if (experiment.Status < ExperimentStatus.Optimizing)
    {
        experiment.Status = ExperimentStatus.Optimizing;
    }

    var loop = services.GetRequiredService<OptimizationLoop>();
    var progress = new Progress<string>(msg => Console.WriteLine($"   {msg}"));

    var optimizationResult = await loop.RunAsync(experiment, progress, async exp =>
    {
        foreach (var candidate in exp.Candidates)
            await store.SaveCandidateAsync(experimentId, candidate);
        for (int i = 0; i < exp.RoundSummaries.Count; i++)
            await store.SaveRunResultsAsync(experimentId, exp.RoundSummaries[i].Round, exp.RoundSummaries[i].RunResults);
        await store.SaveExperimentAsync(exp);
    });

    Console.WriteLine($"\n   Best candidate: {optimizationResult.BestCandidate.Id} " +
                      $"(score: {optimizationResult.BestScore:F3})");

    // ── Stage G: Held-Out Validation ────────────────────────────
    if (experiment.HeldOutResult is not null && experiment.Status >= ExperimentStatus.Validating)
    {
        Console.WriteLine("\n── Stage G: Held-Out Validation (Loaded from checkpoint) ──");
    }
    else
    {
        Console.WriteLine("\n── Stage G: Held-Out Validation ──");
        experiment.Status = ExperimentStatus.Validating;
        var validator = services.GetRequiredService<HeldOutValidator>();
        experiment.HeldOutResult = await validator.ValidateAsync(experiment, optimizationResult.BestCandidate);
        await store.SaveExperimentAsync(experiment);
    }

    Console.WriteLine($"   Original (held-out): {experiment.HeldOutResult.OriginalScore:F3}");
    Console.WriteLine($"   Optimized (held-out): {experiment.HeldOutResult.OptimizedScore:F3}");
    Console.WriteLine($"   Overfit gap: {experiment.HeldOutResult.OverfitGap:+0.000;-0.000}");

    // ── Stage H: Report ─────────────────────────────────────────
    Console.WriteLine("\n── Stage H: Report ──");
    experiment.Status = ExperimentStatus.Complete;
    var reportBuilder = services.GetRequiredService<ReportBuilder>();
    var html = reportBuilder.Build(experiment);

    // Save final report and experiment state
    await store.SaveReportAsync(experimentId, html);
    await store.SaveExperimentAsync(experiment);

    var reportPath = Path.Combine(store.GetExperimentDir(experimentId), "report.html");
    Console.WriteLine($"   ✅ Report saved: {reportPath}");
    Console.WriteLine($"   💰 Total cost: ${experiment.TotalCost:F4}");

    // ── Print the optimized prompt directly in the CLI ───────────
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║                      OPTIMIZED PROMPT                           ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine(optimizationResult.BestCandidate.Text);
    Console.WriteLine();
    Console.WriteLine("──────────────────────────────────────────────────────────────────");
    Console.WriteLine($"   Candidate: {optimizationResult.BestCandidate.Id} | " +
                      $"Score: {optimizationResult.BestScore:F3} | " +
                      $"Round: {optimizationResult.BestCandidate.Round}");
    if (optimizationResult.BestCandidate.ChangeLog is not null)
    {
        Console.WriteLine($"   Changes: {optimizationResult.BestCandidate.ChangeLog}");
    }
    Console.WriteLine("──────────────────────────────────────────────────────────────────");

    return 0;
});

// ─── evaluate command  ───────────────────────────────────────

var evaluateCommand = new Command("evaluate", "Evaluate and score a single prompt without running optimization");
var evalPromptArg = new Argument<FileInfo>("prompt-file", "Path to a file containing the prompt");
var evalRepeatsOption = new Option<int>("--repeats", () => 1, "Repeats per test case");

evaluateCommand.AddArgument(evalPromptArg);
evaluateCommand.AddOption(evalRepeatsOption);

evaluateCommand.Handler = CommandHandler.Create<FileInfo, int, string?>(
    async (promptFile, repeats, provider) =>
{
    if (!promptFile.Exists)
    {
        Console.Error.WriteLine($"❌ File not found: {promptFile.FullName}");
        return 1;
    }

    var promptText = await File.ReadAllTextAsync(promptFile.FullName);
    Console.WriteLine($"🔎 Evaluating prompt from: {promptFile.Name}");

    var services = BuildServices(config, provider);
    var drafter = services.GetRequiredService<RubricDrafter>();
    var classifier = services.GetRequiredService<SuitabilityClassifier>();
    var generator = services.GetRequiredService<TestInputGenerator>();
    var runner = services.GetRequiredService<Runner>();

    var placeholders = Runner.ExtractPlaceholders(promptText);
    Console.WriteLine($"   Placeholders: {(placeholders.Count > 0 ? string.Join(", ", placeholders) : "none")}");

    Console.WriteLine("   Analyzing suitability & drafting rubric...");
    var suitability = await classifier.ClassifyAsync(promptText);
    var rubric = await drafter.DraftAsync(promptText, suitability);

    Console.WriteLine($"   Task: {rubric.TaskDescription}");
    Console.WriteLine("   Generating 5 evaluation inputs...");
    var testCases = await generator.GenerateAsync(promptText, rubric, placeholders, targetCount: 5);

    var candidate = new PromptCandidate { Id = "eval-test", Text = promptText, Round = 0 };
    Console.WriteLine("   Running prompt and scoring...");
    var results = await runner.RunAndScoreAsync(candidate, testCases, rubric, repeats);

    Console.WriteLine("\n📊 Score Table:");
    Console.WriteLine("----------------------------------------------------------------------");
    Console.WriteLine($"{"Test Case",-10} | {"Score",-7} | {"Output Excerpt",-45}");
    Console.WriteLine("----------------------------------------------------------------------");
    foreach (var r in results)
    {
        var outSnippet = r.Outputs.FirstOrDefault()?.Replace("\n", " ") ?? "";
        if (outSnippet.Length > 43) outSnippet = outSnippet[..40] + "...";
        Console.WriteLine($"{r.TestCaseId,-10} | {r.AggregateScore,-7:F2} | {outSnippet,-45}");
        foreach (var cs in r.CriterionScores)
        {
            var icon = cs.Passed ? "✅" : "❌";
            Console.WriteLine($"   {icon} [{cs.CriterionId}]: {cs.Score:F2} — {cs.Reason}");
        }
    }
    Console.WriteLine("----------------------------------------------------------------------");
    var avg = results.Average(r => r.AggregateScore);
    Console.WriteLine($"Mean Aggregate Score: {avg:F3}\n");

    return 0;
});

// ─── list command ────────────────────────────────────────────────────────────

var listCommand = new Command("list", "List all experiments");
listCommand.Handler = CommandHandler.Create(() =>
{
    var store = new ExperimentStore();
    var experiments = store.ListExperiments();

    if (experiments.Count == 0)
    {
        Console.WriteLine("No experiments found.");
        return;
    }

    Console.WriteLine($"Found {experiments.Count} experiment(s):");
    foreach (var id in experiments)
        Console.WriteLine($"  • {id}");
});

// ─── report command ──────────────────────────────────────────────────────────

var reportCommand = new Command("report", "View or regenerate an experiment report");
var expIdArg = new Argument<string>("experiment-id", "The experiment ID");
reportCommand.AddArgument(expIdArg);
reportCommand.Handler = CommandHandler.Create<string>(async (experimentId) =>
{
    var store = new ExperimentStore();
    var experiment = await store.LoadExperimentAsync(experimentId);

    if (experiment is null)
    {
        Console.Error.WriteLine($"❌ Experiment not found: {experimentId}");
        return;
    }

    Console.WriteLine($"Experiment: {experiment.Id}");
    Console.WriteLine($"Status: {experiment.Status}");
    Console.WriteLine($"Rounds: {experiment.RoundSummaries.Count}");
    Console.WriteLine($"Cost: ${experiment.TotalCost:F4}");

    var reportBuilder = new ReportBuilder();
    var html = reportBuilder.Build(experiment);
    await store.SaveReportAsync(experimentId, html);

    var reportPath = Path.Combine(store.GetExperimentDir(experimentId), "report.html");
    if (File.Exists(reportPath))
        Console.WriteLine($"Report: {reportPath}");
});

rootCommand.AddCommand(optimizeCommand);
rootCommand.AddCommand(evaluateCommand);
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(reportCommand);

return await rootCommand.InvokeAsync(args);

// ─── Service wiring with Strategy Pattern ─────────────────────────────────────

static ServiceProvider BuildServices(IConfiguration config, string? providerOverride = null)
{
    var services = new ServiceCollection();

    // 1. Determine active provider strategy
    var providerName = providerOverride
                    ?? config["Llm:Provider"]
                    ?? "Anthropic";

    var providerType = LlmClientFactory.ParseProviderType(providerName);
    Console.WriteLine($"🔌 Active LLM Strategy: {providerType}");

    string apiKey = string.Empty;
    string runnerModel = string.Empty;
    string strongModel = string.Empty;
    string? baseUrl = null;
    string? chatEndpoint = null;
    decimal runnerInputCost = 0m;
    decimal runnerOutputCost = 0m;
    decimal strongInputCost = 0m;
    decimal strongOutputCost = 0m;

    if (providerType == LlmProviderType.Anthropic)
    {
        apiKey = config["Llm:Anthropic:ApiKey"]
              ?? config["Anthropic:ApiKey"]
              ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
              ?? string.Empty;

        baseUrl = config["Llm:Anthropic:BaseUrl"] ?? "https://api.anthropic.com/v1";
        runnerModel = config["Llm:Anthropic:RunnerModel"] ?? "claude-3-5-haiku-20241022";
        strongModel = config["Llm:Anthropic:JudgeModel"] ?? "claude-3-5-sonnet-20241022";

        runnerInputCost = 0.0008m;
        runnerOutputCost = 0.004m;
        strongInputCost = 0.003m;
        strongOutputCost = 0.015m;
    }
    else if (providerType == LlmProviderType.Gemini)
    {
        apiKey = config["Llm:Gemini:ApiKey"]
              ?? config["GEMINI_API_KEY"]
              ?? config["GOOGLE_API_KEY"]
              ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
              ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
              ?? string.Empty;

        baseUrl = config["Llm:Gemini:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta/openai/";
        chatEndpoint = config["Llm:Gemini:ChatEndpoint"] ?? "chat/completions";
        runnerModel = config["Llm:Gemini:RunnerModel"] ?? "gemini-2.5-flash";
        strongModel = config["Llm:Gemini:JudgeModel"] ?? "gemini-2.5-pro";

        // Free tier: $0 cost
        runnerInputCost = 0.0m;
        runnerOutputCost = 0.0m;
        strongInputCost = 0.0m;
        strongOutputCost = 0.0m;
    }
    else if (providerType == LlmProviderType.GenericRest)
    {
        apiKey = config["Llm:GenericRest:ApiKey"]
              ?? config["OPENAI_API_KEY"]
              ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
              ?? string.Empty;

        baseUrl = config["Llm:GenericRest:BaseUrl"] ?? "https://api.openai.com/v1";
        chatEndpoint = config["Llm:GenericRest:ChatEndpoint"] ?? "chat/completions";
        runnerModel = config["Llm:GenericRest:RunnerModel"] ?? "gpt-4o-mini";
        strongModel = config["Llm:GenericRest:JudgeModel"] ?? "gpt-4o";

        runnerInputCost = 0.00015m;
        runnerOutputCost = 0.0006m;
        strongInputCost = 0.0025m;
        strongOutputCost = 0.01m;
    }
    else // Mock
    {
        runnerModel = "mock-runner";
        strongModel = "mock-judge";
    }

    if (providerType != LlmProviderType.Mock && string.IsNullOrWhiteSpace(apiKey))
    {
        throw new InvalidOperationException(
            $"API key is not configured for provider '{providerType}'. " +
            $"Please set it in appsettings.json (under Llm:{providerType}:ApiKey) or via environment variable (e.g. GEMINI_API_KEY, ANTHROPIC_API_KEY, or OPENAI_API_KEY). " +
            $"Alternatively, pass '--provider Mock' to run in mock simulation mode.");
    }

    var runnerConfig = new LlmProviderConfig
    {
        Name = $"Runner ({runnerModel})",
        ModelId = runnerModel,
        ApiKey = apiKey,
        BaseUrl = baseUrl,
        ChatEndpoint = chatEndpoint,
        ProviderType = providerType,
        InputCostPer1K = runnerInputCost,
        OutputCostPer1K = runnerOutputCost
    };

    var strongConfig = new LlmProviderConfig
    {
        Name = $"Strong ({strongModel})",
        ModelId = strongModel,
        ApiKey = apiKey,
        BaseUrl = baseUrl,
        ChatEndpoint = chatEndpoint,
        ProviderType = providerType,
        InputCostPer1K = strongInputCost,
        OutputCostPer1K = strongOutputCost
    };

    var httpClient = new HttpClient();
    services.AddSingleton(httpClient);

    // Create IChatClient strategies using the strategy factory
    var runnerChatClient = LlmClientFactory.Create(providerType, runnerConfig, httpClient, chatEndpoint);
    var strongChatClient = LlmClientFactory.Create(providerType, strongConfig, httpClient, chatEndpoint);

    var runnerClient = new ProviderClient(runnerChatClient, runnerConfig);
    var strongClient = new ProviderClient(strongChatClient, strongConfig);

    // Register deterministic checks
    var deterministicChecks = new IDeterministicCheck[]
    {
        new JsonValidCheck(),
        new MaxLengthCheck(),
        new RequiredFieldsCheck(),
        new ForbiddenPhraseCheck()
    };

    foreach (var check in deterministicChecks)
    {
        services.AddSingleton(check);
    }

    var scorer = new Scorer(deterministicChecks);
    services.AddSingleton(scorer);

    var llmJudge = new LlmJudge(strongClient);
    services.AddSingleton(llmJudge);

    var runner = new Runner(runnerClient, llmJudge, scorer);
    services.AddSingleton(runner);

    var stoppingRules = new StoppingRules();
    services.AddSingleton(stoppingRules);

    var failureAnalyzer = new FailureAnalyzer(strongClient);
    services.AddSingleton(failureAnalyzer);

    var promptRewriter = new PromptRewriter(strongClient);
    services.AddSingleton(promptRewriter);

    services.AddSingleton(new SuitabilityClassifier(strongClient));
    services.AddSingleton(new RubricDrafter(strongClient));
    services.AddSingleton(new TestInputGenerator(strongClient));
    services.AddSingleton(new OptimizationLoop(runner, failureAnalyzer, promptRewriter, stoppingRules));
    services.AddSingleton(new HeldOutValidator(runner));
    services.AddSingleton(new ExperimentStore());
    services.AddSingleton(new DatasetSplitter());
    services.AddSingleton(new ReportBuilder());

    return services.BuildServiceProvider();
}
