Raw prompt
   │
   ▼
[A] SuitabilityClassifier ──► objective / partly / subjective
   ▼
[B] RubricDrafter ──► rubric.json ──► ✋ user edits + approves
   ▼
[C] TestInputGenerator ──► ~25 inputs ──► ✋ user reviews
   │                              │
   │                    DatasetSplitter
   │                     ┌────────┴─────────┐
   │              optimization (~20)    held-out (~5)  ← locked away
   ▼                     │
┌──────────────── loop, 3–5 rounds ────────────────┐
│ [D] Runner → deterministic checks → LlmJudge     │
│ [E] FailureAnalyzer → PromptRewriter (+changelog)│
│ [F] StoppingRules (target / plateau / budget)    │
└──────────────────────────────────────────────────┘
   ▼  best candidate
[G] HeldOutValidator (original vs best, held-out only)
   ▼
[H] ReportBuilder ──► report.html