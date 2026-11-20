using System.Text.Json;
using System.Text.RegularExpressions;

namespace PromptOptimizer.Llm;

/// <summary>
/// Parses JSON responses from LLMs, handling a wide set of common failure modes:
///   • Markdown code fences  (```json … ```)
///   • Thinking / reasoning traces  (&lt;think&gt;…&lt;/think&gt;, &lt;thinking&gt;…&lt;/thinking&gt;)
///   • Prose preamble before the first { or [
///   • Trailing prose after the closing } or ]
///   • Unescaped literal newlines inside JSON string values
///   • BOM / zero-width characters at the start
/// </summary>
public static partial class JsonResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Attempts to parse a model response as JSON of type <typeparamref name="T"/>.
    /// Applies a progressive cleaning pipeline before each parse attempt.
    /// </summary>
    public static JsonParseResult<T> TryParse<T>(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return JsonParseResult<T>.Fail("Response was empty or whitespace-only.");

        // Progressive cleaning pipeline — stop at first success
        var candidates = BuildCandidates(rawResponse);

        JsonException? lastEx = null;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var result = JsonSerializer.Deserialize<T>(candidate, JsonOptions);
                if (result is not null)
                    return JsonParseResult<T>.Ok(result, candidate);
            }
            catch (JsonException ex)
            {
                lastEx = ex;
            }
        }

        return JsonParseResult<T>.Fail(
            $"JSON parse error: {lastEx?.Message ?? "unknown"}",
            candidates.FirstOrDefault());
    }

    // ── Cleaning pipeline ────────────────────────────────────────────────────

    private static IEnumerable<string> BuildCandidates(string raw)
    {
        // 1. Strip BOM / zero-width chars, then trim
        var step1 = raw.TrimStart('\uFEFF', '\u200B', '\u200C', '\u200D', '\uFFFE').Trim();
        yield return step1;

        // 2. Strip thinking / reasoning blocks  <think>…</think>  or  <thinking>…</thinking>
        var step2 = ThinkingBlockRegex().Replace(step1, string.Empty).Trim();
        if (step2 != step1) yield return step2;

        // 3. Strip markdown code fences  ```json … ```  or  ``` … ```
        var step3 = StripCodeFences(step2);
        if (step3 != step2) yield return step3;

        // 4. Extract the first balanced JSON object { … } from whatever remains
        var obj = ExtractFirstJsonObject(step3);
        if (obj is not null && obj != step3) yield return obj;

        // 5. Extract the first balanced JSON array [ … ] as a fallback
        var arr = ExtractFirstJsonArray(step3);
        if (arr is not null && arr != step3) yield return arr;

        // 6. Try to fix unescaped literal newlines inside string values
        if (obj is not null)
        {
            var fixed6 = FixUnescapedNewlines(obj);
            if (fixed6 != obj) yield return fixed6;
        }
    }

    // ── Public helpers (also used by tests) ─────────────────────────────────

    /// <summary>Strips markdown-style code fences (```json … ``` or ``` … ```).</summary>
    public static string StripCodeFences(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var match = CodeFenceRegex().Match(input);
        return match.Success ? match.Groups[1].Value.Trim() : input;
    }

    /// <summary>
    /// Extracts the first balanced JSON object { … } from a string that may contain
    /// prose before or after the JSON.
    /// </summary>
    public static string? ExtractFirstJsonObject(string input)
        => ExtractBalanced(input, '{', '}');

    /// <summary>
    /// Extracts the first balanced JSON array [ … ] from a string that may contain
    /// prose before or after the JSON.
    /// </summary>
    public static string? ExtractFirstJsonArray(string input)
        => ExtractBalanced(input, '[', ']');

    /// <summary>
    /// Replaces literal (unescaped) newlines that appear inside JSON string values
    /// with the \\n escape sequence.  Heuristic — only applied as a last-resort fix.
    /// </summary>
    public static string FixUnescapedNewlines(string input)
        => UnescapedNewlineRegex().Replace(input, m =>
        {
            // Only replace if inside a JSON string (odd number of preceding quote chars)
            var prefix = input[..m.Index];
            var quoteCount = prefix.Count(c => c == '"') - prefix.Split("\\\"").Length + 1;
            return quoteCount % 2 == 1 ? "\\n" : m.Value;
        });

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Walks the string character-by-character to find the first balanced
    /// occurrence of <paramref name="open"/>…<paramref name="close"/>,
    /// skipping over JSON string literals so nested braces inside strings
    /// don't confuse the counter.
    /// </summary>
    private static string? ExtractBalanced(string input, char open, char close)
    {
        var start = input.IndexOf(open);
        if (start < 0) return null;

        int depth = 0;
        bool inString = false;
        bool escape = false;

        for (int i = start; i < input.Length; i++)
        {
            var ch = input[i];

            if (escape) { escape = false; continue; }
            if (ch == '\\' && inString) { escape = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (ch == open)  depth++;
            if (ch == close) depth--;

            if (depth == 0)
                return input[start..(i + 1)].Trim();
        }

        return null; // unbalanced
    }

    // ── Compiled regexes ─────────────────────────────────────────────────────

    [GeneratedRegex(@"```(?:json)?\s*\n?(.*?)\n?\s*```", RegexOptions.Singleline)]
    private static partial Regex CodeFenceRegex();

    [GeneratedRegex(@"<think(?:ing)?>\s*.*?\s*</think(?:ing)?>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkingBlockRegex();

    // Matches a bare (non-escaped) newline or carriage-return
    [GeneratedRegex(@"(?<!\\)[\r\n]")]
    private static partial Regex UnescapedNewlineRegex();
}

/// <summary>
/// Result of attempting to parse a JSON response.
/// </summary>
public sealed class JsonParseResult<T>
{
    public bool Success { get; private init; }
    public T? Value { get; private init; }
    public string? Error { get; private init; }
    public string? CleanedJson { get; private init; }

    public static JsonParseResult<T> Ok(T value, string cleanedJson) => new()
    {
        Success = true,
        Value = value,
        CleanedJson = cleanedJson
    };

    public static JsonParseResult<T> Fail(string error, string? cleanedJson = null) => new()
    {
        Success = false,
        Error = error,
        CleanedJson = cleanedJson
    };
}
