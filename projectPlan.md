# Project 1: Prompt Optimizer
 
## 1.1 The Idea
 
A user writes a prompt. The system analyzes it, works out what "good output" means, builds a test set, measures how well the prompt performs, rewrites it over several rounds, and validates the final version on inputs it never saw during optimization. The user receives an improved prompt, a score comparison, and an explanation of what changed and why.
 
## 1.2 The Problem
 
- **Prompt quality is judged by feel.** People tweak a prompt, try one or two inputs, and decide it "seems better." A change that fixes one case often silently breaks another.
- **Nobody writes test cases.** Building a test set and a definition of success is tedious, so most prompts ship untested.
- **Optimizers usually overfit.** A rewrite tuned against a small test set looks great on that set and mediocre on real inputs, but the user never finds out.
- **Existing helpers are opaque.** Many prompt improvement tools return a rewritten prompt with no visible reasoning, no measurement, and no way to check the claim.
## 1.3 The Solution
 
A transparent, measured pipeline with the user kept in the loop at the one point that matters: agreeing on what "good" means.
 
Principles:
1. **Make the rubric explicit and editable.** Success criteria are inferred, shown to the user, and confirmed before any optimization happens.
2. **Prefer deterministic checks.** Anything that can be verified by code (format validity, length limits, required fields, forbidden phrases) is checked by code. A model-based judge handles only the fuzzy criteria.
3. **Hold out validation data.** A portion of test inputs is never shown to the rewriting step. The final score comes from those inputs only.
4. **Show the evidence.** Every rewrite is tied to specific observed failures, and the report shows which were fixed and which remain.
5. **Know the limits.** Prompts whose quality is purely subjective, or that depend on private data, tools, or multi-turn context, are detected and flagged rather than optimized with false confidence.
## 1.4 Scope
 
**In scope**
- Single-turn prompts with one or more input placeholders
- Tasks with checkable quality: classification, extraction, summarization with constraints, rewriting with rules, formatting, question answering over provided text
- Side-by-side report of original and optimized prompts
**Out of scope**
- Multi-turn conversations, prompts that need external tools or private data
- Purely creative prompts where "better" is taste
- Automatic deployment of the optimized prompt anywhere
## 1.5 Pipeline Design
 
### Stage A: Intake and suitability check
- Accept the prompt text, with the user marking input placeholders.
- Ask the model to classify the prompt: is the task objectively checkable, partly checkable, or subjective? Return a structured verdict with a short reason.
- If subjective, warn the user and offer to continue with reduced confidence labeling.
### Stage B: Task analysis and rubric draft
- The model infers: the task in one sentence, the expected input types, the expected output shape, and a list of success criteria.
- Each criterion is tagged **deterministic** (checkable by code) or **judged** (needs a model).
- Each criterion gets a weight or a hard-fail flag (for example, "output must be valid structured data" is a hard fail).
- Present the rubric to the user for editing. Nothing proceeds until the user approves.
### Stage C: Test input generation
- Generate roughly 25 realistic inputs for the placeholders, covering: typical cases, short and long inputs, ambiguous inputs, inputs with missing information, inputs that try to derail the prompt, and unusual formatting.
- Where useful, generate an expected-answer note per input (for classification or extraction tasks).
- Let the user review, delete, or add inputs.
- Split into an **optimization set** (about 20) and a **held-out set** (about 5, never exposed to the rewriting step).
### Stage D: Run and grade
- Run the current prompt on every input using a cheaper, faster model, since that is the model the prompt will realistically be used with.
- Score each output: deterministic checks first, then a stronger model as judge for the fuzzy criteria. The judge returns a structured result with a score and a one-sentence reason per criterion.
- Run each input more than once if outputs vary, and use the average, because model output is not deterministic.
### Stage E: Failure analysis and rewrite
- Group failures by pattern (for example, "ignores the length limit on long inputs").
- Give the rewriting model: the current prompt, the rubric, and a sample of failing cases with their outputs and judge reasons. Never include the held-out set.
- Request a revised prompt plus a short change log explaining which failure each edit targets.
- Ask for a small, targeted edit, not a full rewrite, so improvements can be attributed.
### Stage F: Iterate with stopping rules
- Repeat D and E for a fixed maximum number of rounds (3 to 5).
- Keep every candidate prompt and its score. Stop early if the score reaches a target, stops improving for two rounds, or the cost budget is hit.
- Select the best candidate by optimization-set score.
### Stage G: Held-out validation
- Score the original prompt and the best candidate on the held-out set only.
- Report the gap between optimization-set improvement and held-out improvement. A large gap means overfitting, and the report must say so.
### Stage H: Report
- Original versus optimized prompt, shown side by side with differences highlighted
- Score table: optimization set and held-out set, before and after, per criterion
- Per-round history chart
- List of failures fixed, failures remaining, and any regressions introduced
- Cost and call count for the run
- A plain-language confidence statement, including the judge-bias caveat
## 1.6 Data Model (conceptual)
 
- **Prompt candidate:** text, parent candidate, change log, round number
- **Rubric:** list of criteria, each with type (deterministic or judged), weight, hard-fail flag
- **Test case:** input values, optional expected answer, set membership (optimization or held-out)
- **Run result:** candidate, test case, output, per-criterion scores, judge reasons, cost
- **Experiment:** all of the above plus budget, round limit, and final report
## 1.7 Implementation Plan
 
### Day 1: Foundations and evaluation core
Goal: be able to score a single prompt reliably before building any optimization.
- Define the rubric structure and the structured result format for the judge.
- Write the deterministic checks for a small set of common criteria (format validity, length limits, required content, forbidden content).
- Write the judge prompt for fuzzy criteria. Test it against a handful of hand-labeled outputs and confirm it agrees with your own judgment.
- Build the run-and-grade loop for one prompt over a manually written set of five inputs.
- **Deliverable:** a score table for a prompt you wrote by hand.
- **Check:** deliberately degrade a prompt and confirm the score drops.
### Day 2: Intake, rubric inference, and test generation
Goal: go from a raw prompt to an approved rubric and a test set.
- Build the suitability classifier and the task analysis step.
- Build rubric drafting with deterministic versus judged tagging.
- Build test input generation with the five categories listed above, and the split into optimization and held-out sets.
- Add the user review points for rubric and test inputs.
- **Deliverable:** raw prompt in, approved rubric and test set out.
- **Check:** read the generated inputs yourself. If they are bland or repetitive, improve the generation instructions before moving on.
### Day 3: Optimization loop
Goal: improve a prompt automatically and keep the evidence.
- Build failure grouping and the rewrite step with the change log requirement.
- Build the iteration controller: round limit, early stop, budget cap, candidate history.
- Confirm the held-out set is structurally unreachable by the rewrite step (separate data path, not just a promise in the instructions).
- **Deliverable:** a multi-round run that produces a candidate history.
- **Check:** run it on three very different prompts. Note where it helps, where it does nothing, and where it makes things worse.
### Day 4: Validation, report, and polish
Goal: trustworthy output and a presentable result.
- Build held-out validation and the overfitting gap statement.
- Build the report with side-by-side prompts, score tables, round history, and the confidence statement.
- Write the README: the problem, the pipeline diagram, honest limitations, and results from your three test prompts.
- Record cost per run and typical duration.
- **Deliverable:** an end-to-end run a stranger could follow, plus documentation.
## 1.8 Evaluating the Optimizer Itself
 
An optimizer needs its own tests, otherwise the project proves nothing.
- Prepare 5 to 8 starter prompts of varying quality across different task types.
- For each, record the held-out improvement, the number of rounds, and the cost.
- Include at least one case where optimization failed or made no difference, and explain why. Honest failures make the results more believable.
- Test the judge: take 20 outputs you scored by hand and compare. Report the agreement rate.
## 1.9 Risks and Mitigations
 
| Risk | Mitigation |
|---|---|
| Judge favors longer or more polished answers | Deterministic checks first; length-controlled criteria; report agreement with hand scores |
| Overfitting to the test set | Held-out set, unreachable by the rewrite step; report the gap |
| Cost grows quickly | Budget cap, round limit, cheaper model for runs, small test sets |
| Generated test inputs are unrealistic | User review step; categories that force variety |
| Rewrites change the prompt's intent | Require a change log; show a diff; have the user approve the final version |
| Noisy scores from nondeterministic output | Repeat runs and average; only count improvements above a noise threshold |
 
## 1.10 Success Criteria
- Measurable held-out improvement on a majority of your starter prompts
- At least one documented failure case with analysis
- Judge agreement with your hand scores reported
- A clear, honest README with a pipeline diagram and cost figures
## 1.11 How to Present It
Lead with one concrete result, such as "raised held-out pass rate from 54% to 83% on a support-email classification prompt, with a validated 5-point gap between optimization and held-out scores." Then describe the design decisions: why held-out validation, why deterministic checks first, why the rubric is user-approved.
 
---