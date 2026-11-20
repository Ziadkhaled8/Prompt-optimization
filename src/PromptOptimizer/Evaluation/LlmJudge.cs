using PromptOptimizer.Llm;
using PromptOptimizer.Models;

namespace PromptOptimizer.Evaluation;

/// <summary>
/// Uses an LLM to evaluate fuzzy/subjective criteria that can't be checked deterministically.
/// Returns a structured score per criterion.
/// </summary>
public sealed class LlmJudge
{
    private readonly ProviderClient _judgeClient;

    public LlmJudge(ProviderClient judgeClient)
    {
        _judgeClient = judgeClient;
    }

    /// <summary>
    /// Judges a single output against the judged criteria in the rubric.
    /// </summary>
    public async Task<IReadOnlyList<CriterionScore>> JudgeAsync(
        string promptText,
        string output,
        Rubric rubric,
        TestCase testCase,
        CancellationToken ct = default)
    {
        var judgedCriteria = rubric.Criteria
            .Where(c => c.Type == CriterionType.Judged)
            .ToList();

        if (judgedCriteria.Count == 0)
            return [];

        var systemPrompt = BuildJudgeSystemPrompt();
        var userMessage = BuildJudgeUserMessage(promptText, output, rubric, testCase, judgedCriteria);

        var scores = await _judgeClient.ChatJsonAsync<JudgeResponse>(
            systemPrompt, userMessage, ct: ct);

        return scores.Scores.Select(s => new CriterionScore
        {
            CriterionId = s.CriterionId,
            Score = Math.Clamp(s.Score, 0.0, 1.0),
            Passed = s.Score >= 0.7, // threshold for passing
            Reason = s.Reason
        }).ToList();
    }

    private static string BuildJudgeSystemPrompt() =>
        """
        You are a precise, objective evaluation judge. You evaluate LLM outputs against specific success criteria.
        
        Rules:
        - Score each criterion from 0.0 (complete failure) to 1.0 (perfect).
        - Be strict but fair. A score of 0.7+ means "passes".
        - Provide a one-sentence reason for each score.
        - Do NOT favor longer or more elaborate answers unless the criterion requires it.
        - Focus ONLY on what each criterion asks for.
        - Respond with ONLY a JSON object matching the required format.
        """;

    private static string BuildJudgeUserMessage(
        string promptText,
        string output,
        Rubric rubric,
        TestCase testCase,
        List<Criterion> judgedCriteria)
    {
        var inputsDisplay = string.Join("\n",
            testCase.Inputs.Select(kv => $"  {kv.Key}: {kv.Value}"));

        var criteriaDisplay = string.Join("\n",
            judgedCriteria.Select((c, i) =>
                $"  {i + 1}. [{c.Id}] {c.Description} (weight: {c.Weight}, hard-fail: {c.HardFail})"));

        var expectedNote = string.IsNullOrEmpty(testCase.ExpectedNote)
            ? "None"
            : testCase.ExpectedNote;

        return
            $"Task: {rubric.TaskDescription}\n" +
            $"Expected output shape: {rubric.ExpectedOutputShape}\n" +
            $"\n" +
            $"Prompt used:\n{promptText}\n" +
            $"\n" +
            $"Input values:\n{inputsDisplay}\n" +
            $"\n" +
            $"Expected answer hint: {expectedNote}\n" +
            $"\n" +
            $"Model output to evaluate:\n{output}\n" +
            $"\n" +
            $"Criteria to judge:\n{criteriaDisplay}\n" +
            $"\n" +
            "Respond with JSON:\n" +
            "{\n" +
            "  \"scores\": [\n" +
            "    { \"criterionId\": \"<id>\", \"score\": <0.0-1.0>, \"reason\": \"<one sentence>\" }\n" +
            "  ]\n" +
            "}";
    }

    // Internal DTO for JSON deserialization
    private sealed class JudgeResponse
    {
        public required List<JudgeScore> Scores { get; init; }
    }

    private sealed class JudgeScore
    {
        public required string CriterionId { get; init; }
        public required double Score { get; init; }
        public required string Reason { get; init; }
    }
}
