# Claude AI Context & Engineering Guide: PromptOptimizer

This document serves as the primary technical context and engineering reference for AI agents and developers working on the **PromptOptimizer** codebase.

---

## 1. Project Overview & Philosophy

**PromptOptimizer** is an automated, measured, and transparent prompt optimization pipeline for LLMs built on .NET 10.

Most prompt engineering is done "by feel" without regression testing or validation. When people tweak a prompt to fix one edge case, they frequently break another without knowing it. PromptOptimizer treats prompts as code:
1. **Explicit & User-Approved Rubrics**: Task requirements are parsed into deterministic (code-based) and judged (LLM-evaluated) criteria. The user inspects and approves this definition of "good" before optimization begins.
2. **Deterministic Checks First**: Verifiable constraints (valid JSON, max length limits, required keywords, forbidden phrases) are evaluated deterministically in C# with zero token cost and zero judge bias.
3. **Strict Held-Out Validation**: Generated test sets are split into an **Optimization set** (~80%) and a **Held-Out set** (~20%). The held-out set is strictly quarantined from the rewrite step. Overfitting is explicitly measured and reported via an "Overfit Gap" metric.
4. **Evidence-Based Rewriting**: Every rewrite is driven by observed failure patterns with concrete failing examples. Rewrites are small, targeted diffs accompanied by explicit change logs.
5. **Subjectivity Guardrails**: Prompts with purely aesthetic or subjective quality criteria are flagged upfront (Stage A).
6. **Continuous Checkpointing & Resumption**: Results are saved to disk after every stage and after each optimization round. If interrupted (e.g. rate limit, crash, user pause), experiments resume seamlessly from the last checkpoint using `--resume`.
7. **Direct CLI Output**: The final optimized prompt is printed directly to the CLI upon completion so the user does not have to open the HTML report to grab the result.

---

## 2. Pipeline Stages

```text
Raw prompt (with {{placeholders}})
   │
   ▼
[Stage A] SuitabilityClassifier ──► Objective / PartlyCheckable / Subjective
   │                                [💾 Checkpoint: prompt.original.md, Status: SuitabilityChecked]
   ▼
[Stage B] RubricDrafter ──────────► rubric.json ──► ✋ User approves/edits
   │                                [💾 Checkpoint: rubric.json, Status: RubricApproved]
   ▼
[Stage C] TestInputGenerator ────► ~25 inputs
   │                                     │
   │                               DatasetSplitter
   │                               ┌─────┴──────────────┐
   │                       Optimization (~80%)     Held-Out (~20%)  ← Quarantined
   │                               │
   │                               [💾 Checkpoint: tests.*.json, Status: TestCasesApproved]
   ▼                               │
┌── Loop (3–5 rounds) ─────────────┘
│ [Stage D] Runner (cheaper model, repeats for averaging)
│           ├── Deterministic Checks (code)
│           └── LlmJudge (fuzzy criteria)
│ [Stage E] FailureAnalyzer (groups failure patterns)
│           └── PromptRewriter (small targeted edits + change log)
│ [Stage F] StoppingRules (Target reached / Plateau / Budget / Max rounds)
│           [💾 Checkpoint per round: candidates/round-N.md, runs/round-N.json, Status: Optimizing]
└──────────────────────────────────┐
   ▼ Best candidate                │
[Stage G] HeldOutValidator ────────┘ (Original vs Best on Held-Out set only)
   │                                [💾 Checkpoint: HeldOutResult, Status: Validating]
   ▼
[Stage H] ReportBuilder ───────────► report.html (side-by-side diff, tables, charts, overfit statement)
                                     [💾 Checkpoint: Status: Complete]
                                     └──► 🖥️ Terminal: Prints optimized prompt, score & diff directly in CLI
```

---

## 3. Directory & File Structure

```text
prompt_eval/
├── PromptOptimizer.slnx            # .NET 10 solution file
├── src/PromptOptimizer/            # Core project
│   ├── Models/                     # Pure data records & DTOs
│   │   ├── CriterionType.cs        # Deterministic | Judged
│   │   ├── Criterion.cs            # Id, Description, Weight, HardFail, CheckConfig
│   │   ├── Rubric.cs               # Task description, output shape, criteria list
│   │   ├── SuitabilityVerdict.cs   # Objective, PartlyCheckable, Subjective
│   │   ├── TestCase.cs             # Id, Inputs dictionary, ExpectedNote, Set membership
│   │   ├── TestSetMembership.cs    # Optimization | HeldOut
│   │   ├── PromptCandidate.cs      # Id, Text, ParentId, ChangeLog, Round
│   │   ├── CriterionScore.cs       # CriterionId, Score (0.0-1.0), Passed, Reason
│   │   ├── RunResult.cs            # CandidateId, TestCaseId, Outputs, AggregateScore, Cost
│   │   ├── RoundSummary.cs         # Per-round mean scores, criterion breakdowns
│   │   ├── FailurePattern.cs       # Failure groups for rewriter
│   │   └── Experiment.cs           # Root experiment container & status enum
│   ├── Llm/                        # LLM abstraction and provider client
│   │   ├── ProviderClient.cs       # Wraps IChatClient with retries, timeouts, token/cost tracking
│   │   └── JsonResponseParser.cs   # Strips markdown fences, robust JSON parsing & error recovery
│   ├── Evaluation/                 # Stage D evaluation engine
│   │   ├── Checks/                 # IDeterministicCheck implementations
│   │   │   ├── IDeterministicCheck.cs
│   │   │   ├── JsonValidCheck.cs
│   │   │   ├── MaxLengthCheck.cs
│   │   │   ├── RequiredFieldsCheck.cs
│   │   │   └── ForbiddenPhraseCheck.cs
│   │   ├── Scorer.cs               # Weighted averages & hard-fail zeroing logic
│   │   ├── LlmJudge.cs             # LLM-based evaluation for fuzzy criteria
│   │   └── Runner.cs               # Template substitution, multi-repeat execution
│   ├── Intake/                     # Stages A & B
│   │   ├── SuitabilityClassifier.cs
│   │   └── RubricDrafter.cs
│   ├── TestGeneration/             # Stage C
│   │   ├── TestInputGenerator.cs   # 5 categories: typical, short_long, ambiguous, adversarial, unusual_format
│   │   └── DatasetSplitter.cs      # Balanced stratified splitting
│   ├── Optimization/               # Stages E & F
│   │   ├── FailureAnalyzer.cs      # Pattern grouping of low-scoring runs
│   │   ├── PromptRewriter.cs       # Targeted prompt modifier with change logs
│   │   ├── StoppingRules.cs        # Termination logic (max rounds, target score, budget, plateau)
│   │   └── OptimizationLoop.cs     # Main iteration coordinator
│   ├── Validation/                 # Stage G
│   │   └── HeldOutValidator.cs     # Evaluates candidate on quarantined held-out set
│   ├── Reporting/                  # Stage H
│   │   └── ReportBuilder.cs        # Self-contained HTML report with diffs & confidence notes
│   ├── Storage/
│   │   └── ExperimentStore.cs      # Disk persistence matching experiments/ folder format
│   ├── appsettings.json            # Configuration template
│   └── Program.cs                  # System.CommandLine CLI entry point
├── tests/PromptOptimizer.Tests/    # xUnit test suite
│   ├── Evaluation/                 # Checks, Scorer, Runner tests
│   ├── Optimization/               # StoppingRules tests
│   ├── TestGeneration/             # DatasetSplitter tests
│   └── Llm/                        # JsonResponseParser tests
├── meta-prompts/                   # Meta-prompts used by the system (.md)
│   ├── suitability.md              # Stage A prompt
│   ├── rubric-draft.md             # Stage B prompt
│   ├── testgen.md                  # Stage C prompt
│   ├── judge.md                    # Stage D LLM judge prompt
│   └── rewrite.md                  # Stage E rewriter prompt
├── samples/                        # Starter test prompts
│   ├── support-classifier.md
│   └── extract-invoice.md
├── experiments/                    # Artifacts from optimization runs
│   └── <experiment-id>/            # Per-run folder
│       ├── prompt.original.md
│       ├── rubric.json
│       ├── tests.optimization.json
│       ├── tests.heldout.json
│       ├── candidates/round-N.md
│       ├── runs/round-N.json
│       ├── experiment.json
│       └── report.html
└── claude.md                       # This document
```

---

## 4. Key Architectural Invariants & Rules

When modifying or extending this codebase, adhere to these rules:

### 1. Quarantined Held-Out Set
- **NEVER** expose test cases marked `TestSetMembership.HeldOut` to `FailureAnalyzer`, `PromptRewriter`, or `OptimizationLoop`.
- Only `HeldOutValidator` reads `tests.heldout.json`.
- The Overfit Gap is calculated as:
  $$\text{Overfit Gap} = \Delta\text{Optimization Score} - \Delta\text{Held-Out Score}$$
  A positive gap $> 0.10$ must trigger an overfitting warning in the report.

### 2. Hard-Fail Criteria
- If a criterion has `HardFail = true` and fails (`Passed == false`), the aggregate score for that test case output is strictly **0.0**, regardless of other criteria scores.

### 3. Prompt Placeholders
- Placeholders in prompts use the `{{placeholder_name}}` syntax.
- `Runner.SubstitutePlaceholders` performs case-insensitive token replacement.
- Rewriter meta-prompts must always instruct the model to preserve all original `{{placeholder}}` variables.

### 4. Deterministic Checks Before LLM Calls
- Always prefer code-based checks over LLM calls. Deterministic checks have zero cost, zero latency, and zero variance.
- When adding new checks, implement `IDeterministicCheck` and register them in DI.

### 5. Multi-Repeat Averaging
- LLM outputs can be non-deterministic. Test cases should be evaluated with `RepeatsPerRun >= 2`. Scores are averaged and pass/fail is determined by majority vote.

### 6. Continuous Checkpointing & Fault Tolerance
- Checkpoints are saved immediately after each stage transition (`SuitabilityChecked`, `RubricApproved`, `TestCasesApproved`, `Validating`, `Complete`) and after every round within the optimization loop.
- If a run is interrupted or fails (e.g. rate limit, network timeout, process termination), re-running the command with `--resume` restores the experiment state and continues from the last completed stage or round without re-spending API budget.
- Experiments with existing state on disk cannot be overwritten accidentally without `--resume`.

### 7. Direct CLI Result Delivery
- Upon completion of Stage H, the final optimized prompt text, best candidate identifier, mean score, round count, and change log are displayed in a formatted box directly in the CLI so users don't need to open the HTML report.

---

## 5. Technology Stack & Strategy Pattern for LLM Providers

- **Language / Runtime**: C# 13 on .NET 10 (`net10.0`).
- **AI Abstractions & Strategy Pattern**:
  `Microsoft.Extensions.AI.IChatClient` acts as the strategy interface. Multiple concrete strategy implementations exist:
  - **`Gemini`**: Strategy targeting the Google Gemini OpenAI-compatible endpoint (`https://generativelanguage.googleapis.com/v1beta/openai/`). Works with Google's free tier (`gemini-2.5-flash`, `gemini-2.5-pro`) with $0 tracked cost.
  - **`AnthropicChatClient`**: Strategy powered by the official Anthropic C# SDK (`Anthropic` package). Integrates with `AnthropicClient` and its native `AsIChatClient()` adapter for Claude models (Claude 3.5 Sonnet, Claude 3.5 Haiku, etc.).
  - **`GenericRestChatClient`**: General-purpose strategy connecting to any OpenAI-compatible `/chat/completions` REST API endpoint. Works with OpenAI, Ollama, Groq, DeepSeek, Together AI, Mistral, vLLM, and local proxies.
  - **`MockChatClient`**: Deterministic simulation strategy for offline testing, debugging, and verification without consuming API credits.
  - **`LlmClientFactory`**: Factory to instantiate and switch between strategies at runtime based on configuration or the `--provider` CLI flag.
- **CLI Framework**: `System.CommandLine` & `System.CommandLine.NamingConventionBinder` (`2.0.0-beta4.22272.1`).
- **Unit Testing**: xUnit with `Microsoft.NET.Test.Sdk`.

---

## 6. How to Run & Test

### Build the Solution
```powershell
dotnet build PromptOptimizer.slnx
```

### Run All Unit Tests
```powershell
dotnet test tests/PromptOptimizer.Tests/PromptOptimizer.Tests.csproj
```

### Run the CLI
```powershell
# Evaluate a single prompt with Google Gemini (Free Tier)
dotnet run --project src/PromptOptimizer -- evaluate samples/support-classifier.md --provider Gemini

# Optimize a prompt with Google Gemini
dotnet run --project src/PromptOptimizer -- optimize samples/support-classifier.md --provider Gemini --name "gemini-classifier" --max-rounds 3

# Evaluate with offline Mock (simulation without API calls)
dotnet run --project src/PromptOptimizer -- evaluate samples/support-classifier.md --provider Mock

# Full optimization loop with Anthropic (Claude)
dotnet run --project src/PromptOptimizer -- optimize samples/support-classifier.md --provider Anthropic --name "test-classifier" --max-rounds 3 --budget 2.00

# Resume an interrupted experiment from its last checkpoint
dotnet run --project src/PromptOptimizer -- optimize samples/support-classifier.md --name "test-classifier" --resume

# Full optimization loop with Generic REST (e.g. OpenAI, Ollama, Groq)
dotnet run --project src/PromptOptimizer -- optimize samples/support-classifier.md --provider GenericRest

# List past experiments
dotnet run --project src/PromptOptimizer -- list

# Inspect experiment report
dotnet run --project src/PromptOptimizer -- report test-classifier
```

---

## 7. Configuration (`appsettings.json`)

```json
{
  "Llm": {
    "Provider": "Gemini",
    "Gemini": {
      "ApiKey": "your-gemini-api-key-here",
      "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/openai/",
      "ChatEndpoint": "chat/completions",
      "RunnerModel": "gemini-2.5-flash",
      "JudgeModel": "gemini-2.5-pro",
      "AnalyzerModel": "gemini-2.5-pro"
    },
    "Anthropic": {
      "ApiKey": "your-anthropic-api-key",
      "BaseUrl": "https://api.anthropic.com/v1",
      "RunnerModel": "claude-3-5-haiku-20241022",
      "JudgeModel": "claude-3-5-sonnet-20241022",
      "AnalyzerModel": "claude-3-5-sonnet-20241022"
    }
  }
}
```
Environment variable overrides: `GEMINI_API_KEY` (or `GOOGLE_API_KEY`), `ANTHROPIC_API_KEY`, or `OPENAI_API_KEY`.
