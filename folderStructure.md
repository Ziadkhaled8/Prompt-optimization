PromptOptimizer/
├── src/PromptOptimizer/
│   ├── Models/                      # plain data, no logic
│   │   ├── Rubric.cs, Criterion.cs  # type: Deterministic | Judged, weight, hardFail
│   │   ├── PromptCandidate.cs       # text, parentId, changeLog, round
│   │   ├── TestCase.cs              # inputs, expectedNote, set
│   │   ├── RunResult.cs             # output, per-criterion scores, reasons, cost
│   │   └── Experiment.cs            # budget, round limit, status
│   ├── Llm/
│   │   ├── ILlmClient.cs
│   │   ├── ProviderClient.cs       # retries, timeouts, token + cost tracking
│   │   └── JsonResponseParser.cs    # strips fences, validates, re-asks on bad JSON
│   ├── Evaluation/                  # Day 1
│   │   ├── Checks/                  # JsonValidCheck, MaxLengthCheck,
│   │   │                            # RequiredFieldsCheck, ForbiddenPhraseCheck
│   │   ├── LlmJudge.cs
│   │   ├── Runner.cs                # runs prompt on inputs (repeats + averages)
│   │   └── Scorer.cs                # weights, hard-fails, aggregate score
│   ├── Intake/                      # Day 2
│   │   ├── SuitabilityClassifier.cs
│   │   └── RubricDrafter.cs
│   ├── TestGeneration/              # Day 2
│   │   ├── TestInputGenerator.cs    # 5 categories for variety
│   │   └── DatasetSplitter.cs
│   ├── Optimization/                # Day 3
│   │   ├── FailureAnalyzer.cs
│   │   ├── PromptRewriter.cs
│   │   ├── StoppingRules.cs
│   │   └── OptimizationLoop.cs
│   ├── Validation/                  # Day 4
│   │   └── HeldOutValidator.cs
│   ├── Reporting/                   # Day 4
│   │   └── ReportBuilder.cs         # HTML: diff, tables, round chart
│   ├── Storage/
│   │   └── ExperimentStore.cs       # reads/writes JSON per experiment
│   └── Program.cs                   # CLI commands
├── meta-prompts/                    # the prompts YOUR system uses, as .md files
│   ├── suitability.md, rubric-draft.md, testgen.md
│   ├── judge.md, rewrite.md
├── samples/                         # 5–8 starter prompts + hand-scored outputs
├── tests/                           # xUnit: checks, scorer, splitter, stopping rules
├── experiments/                     # one folder per run (below)
└── README.md