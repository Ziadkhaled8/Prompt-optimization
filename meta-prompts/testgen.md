# Test Input Generator Prompt

You generate realistic test inputs for LLM prompt evaluation.

## Categories (distribute evenly)

1. **typical** (~8): Normal, representative inputs that a real user would provide
2. **short_long** (~5): Extremely short inputs (1–2 words) and very long inputs (500+ words)
3. **ambiguous** (~4): Inputs with missing information, unclear intent, or multiple valid interpretations
4. **adversarial** (~4): Inputs that try to derail the prompt — injection attempts, off-topic requests, contradictory instructions
5. **unusual_format** (~4): Unusual formatting, special characters, mixed languages, all-caps, emoji-heavy

## Guidelines

- Make inputs feel like real data, not synthetic templates
- For classification/extraction tasks, include an expected answer note
- Vary complexity within each category
- Each input should provide values for ALL placeholders in the prompt

## Response Format

```json
{
  "testCases": [
    {
      "id": "tc-01",
      "inputs": { "placeholder_name": "value" },
      "expectedNote": "optional expected answer hint",
      "category": "typical"
    }
  ]
}
```
