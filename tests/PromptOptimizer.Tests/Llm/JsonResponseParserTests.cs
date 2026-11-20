using PromptOptimizer.Llm;

namespace PromptOptimizer.Tests.Llm;

public class JsonResponseParserTests
{
    private class SamplePayload
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    // ── StripCodeFences ──────────────────────────────────────────────────────

    [Fact]
    public void StripCodeFences_WithJsonTag_RemovesFences()
    {
        var input = "```json\n{\"name\": \"test\", \"value\": 10}\n```";
        var result = JsonResponseParser.StripCodeFences(input);
        Assert.Equal("{\"name\": \"test\", \"value\": 10}", result.Trim());
    }

    [Fact]
    public void StripCodeFences_BareFences_RemovesFences()
    {
        var input = "```\n{\"name\": \"test\", \"value\": 10}\n```";
        var result = JsonResponseParser.StripCodeFences(input);
        Assert.Equal("{\"name\": \"test\", \"value\": 10}", result.Trim());
    }

    // ── TryParse — happy paths ───────────────────────────────────────────────

    [Fact]
    public void TryParse_ValidJsonWithFences_ParsesSuccessfully()
    {
        var input = "```json\n{\"name\": \"Antigravity\", \"value\": 42}\n```";
        var result = JsonResponseParser.TryParse<SamplePayload>(input);

        Assert.True(result.Success);
        Assert.Equal("Antigravity", result.Value!.Name);
        Assert.Equal(42, result.Value.Value);
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsFailureWithError()
    {
        var input = "Not valid JSON at all";
        var result = JsonResponseParser.TryParse<SamplePayload>(input);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // ── TryParse — LLM failure modes ────────────────────────────────────────

    [Fact]
    public void TryParse_ProseBeforeJson_ExtractsObject()
    {
        // Some models emit "Here is your JSON:" before the actual object
        var input = "Here is the JSON you requested:\n{\"name\": \"test\", \"value\": 7}";
        var result = JsonResponseParser.TryParse<SamplePayload>(input);

        Assert.True(result.Success);
        Assert.Equal("test", result.Value!.Name);
        Assert.Equal(7, result.Value.Value);
    }

    [Fact]
    public void TryParse_ThinkingBlockBeforeJson_ParsesSuccessfully()
    {
        var input = "<think>Let me think about this…</think>\n{\"name\": \"ok\", \"value\": 1}";
        var result = JsonResponseParser.TryParse<SamplePayload>(input);

        Assert.True(result.Success);
        Assert.Equal("ok", result.Value!.Name);
    }

    [Fact]
    public void TryParse_ThinkingBlockVariant_ParsesSuccessfully()
    {
        var input = "<thinking>reasoning here</thinking>\n{\"name\": \"ok\", \"value\": 2}";
        var result = JsonResponseParser.TryParse<SamplePayload>(input);

        Assert.True(result.Success);
        Assert.Equal("ok", result.Value!.Name);
    }

    [Fact]
    public void TryParse_ProseAfterJson_ExtractsObject()
    {
        var input = "{\"name\": \"clean\", \"value\": 5}\nHope that helps!";
        var result = JsonResponseParser.TryParse<SamplePayload>(input);

        Assert.True(result.Success);
        Assert.Equal("clean", result.Value!.Name);
    }

    [Fact]
    public void TryParse_BomPrefix_ParsesSuccessfully()
    {
        // BOM character at the very start
        var input = "\uFEFF{\"name\": \"bom\", \"value\": 9}";
        var result = JsonResponseParser.TryParse<SamplePayload>(input);

        Assert.True(result.Success);
        Assert.Equal("bom", result.Value!.Name);
    }

    [Fact]
    public void ExtractFirstJsonObject_NestedBraces_ReturnsFullObject()
    {
        var input = "prefix {\"name\": \"nested\", \"inner\": {\"x\": 1}} suffix";
        var extracted = JsonResponseParser.ExtractFirstJsonObject(input);

        Assert.NotNull(extracted);
        Assert.StartsWith("{", extracted);
        Assert.EndsWith("}", extracted);
        Assert.Contains("\"inner\"", extracted);
    }

    [Fact]
    public void ExtractFirstJsonObject_BracesInStringValue_DoesNotConfuseDepth()
    {
        var input = "{\"name\": \"has { brace } inside\", \"value\": 3}";
        var extracted = JsonResponseParser.ExtractFirstJsonObject(input);

        // Should extract the whole object, not stop at the brace inside the string
        Assert.Equal(input, extracted);
    }
}
