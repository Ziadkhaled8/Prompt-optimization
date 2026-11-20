experiments/2026-11-20-support-classifier/
├── prompt.original.md
├── rubric.json                 # after user approval
├── tests.optimization.json
├── tests.heldout.json          # only HeldOutValidator reads this
├── candidates/round-0.md … round-N.md   # each with its change log
├── runs/round-0.json … round-N.json     # outputs, scores, judge reasons, cost
└── report.html