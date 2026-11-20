using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using PromptOptimizer.Llm;
using PromptOptimizer.Llm.Strategies;

namespace PromptOptimizer.Tests.Llm;

public class LlmStrategyTests
{
    [Fact]
    public void LlmClientFactory_ParseProviderType_ParsesKnownAliases()
    {
        Assert.Equal(LlmProviderType.Anthropic, LlmClientFactory.ParseProviderType("Anthropic"));
        Assert.Equal(LlmProviderType.Anthropic, LlmClientFactory.ParseProviderType("anthropic"));
        Assert.Equal(LlmProviderType.Gemini, LlmClientFactory.ParseProviderType("Gemini"));
        Assert.Equal(LlmProviderType.Gemini, LlmClientFactory.ParseProviderType("gemini"));
        Assert.Equal(LlmProviderType.Gemini, LlmClientFactory.ParseProviderType("Google"));
        Assert.Equal(LlmProviderType.GenericRest, LlmClientFactory.ParseProviderType("GenericRest"));
        Assert.Equal(LlmProviderType.GenericRest, LlmClientFactory.ParseProviderType("OpenAI"));
        Assert.Equal(LlmProviderType.GenericRest, LlmClientFactory.ParseProviderType("Ollama"));
        Assert.Equal(LlmProviderType.GenericRest, LlmClientFactory.ParseProviderType("Groq"));
        Assert.Equal(LlmProviderType.Mock, LlmClientFactory.ParseProviderType("Mock"));
    }

    [Fact]
    public void LlmClientFactory_Create_ReturnsExpectedStrategyType()
    {
        var config = new LlmProviderConfig
        {
            Name = "Test",
            ModelId = "test-model",
            ApiKey = "test-key"
        };

        var anthropicClient = LlmClientFactory.Create(LlmProviderType.Anthropic, config);
        Assert.IsType<AnthropicChatClient>(anthropicClient);

        var geminiClient = LlmClientFactory.Create(LlmProviderType.Gemini, config);
        Assert.IsType<GenericRestChatClient>(geminiClient);

        var restClient = LlmClientFactory.Create(LlmProviderType.GenericRest, config);
        Assert.IsType<GenericRestChatClient>(restClient);

        var mockClient = LlmClientFactory.Create(LlmProviderType.Mock, config);
        Assert.IsType<MockChatClient>(mockClient);
    }

    [Fact]
    public async Task MockChatClient_ReturnsConfiguredCannedResponse()
    {
        var mock = new MockChatClient(new[] { "{\"test\": 123}" });
        var response = await mock.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") });

        Assert.Equal("{\"test\": 123}", response.Text);
        Assert.Equal(100, response.Usage?.InputTokenCount);
        Assert.Equal(50, response.Usage?.OutputTokenCount);
    }

    [Fact]
    public async Task GenericRestChatClient_SendsOpenAiFormattedRequest()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new TestHttpMessageHandler(async (req, ct) =>
        {
            capturedRequest = req;
            capturedBody = await req.Content!.ReadAsStringAsync(ct);

            var fakeResponse = """
                {
                  "id": "chatcmpl-123",
                  "model": "gpt-4o-mini",
                  "choices": [
                    { "message": { "role": "assistant", "content": "REST response" } }
                  ],
                  "usage": { "prompt_tokens": 15, "completion_tokens": 5, "total_tokens": 20 }
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fakeResponse, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        using var client = new GenericRestChatClient("sk-test-key", "gpt-4o-mini", httpClient: httpClient);

        var response = await client.GetResponseAsync(new[]
        {
            new ChatMessage(ChatRole.System, "Be concise"),
            new ChatMessage(ChatRole.User, "Hello")
        });

        Assert.Equal("REST response", response.Text);
        Assert.Equal(15, response.Usage?.InputTokenCount);
        Assert.Equal(5, response.Usage?.OutputTokenCount);

        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("sk-test-key", capturedRequest.Headers.Authorization?.Parameter);

        using var doc = JsonDocument.Parse(capturedBody!);
        var root = doc.RootElement;
        Assert.Equal("gpt-4o-mini", root.GetProperty("model").GetString());
        Assert.Equal(2, root.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task AnthropicChatClient_SeparatesSystemAndSendsHeaders()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new TestHttpMessageHandler(async (req, ct) =>
        {
            capturedRequest = req;
            capturedBody = await req.Content!.ReadAsStringAsync(ct);

            var fakeResponse = """
                {
                  "id": "msg-123",
                  "model": "claude-3-5-sonnet",
                  "content": [
                    { "type": "text", "text": "Anthropic response" }
                  ],
                  "usage": { "input_tokens": 30, "output_tokens": 12 }
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fakeResponse, System.Text.Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/v1/") };
        using var client = new AnthropicChatClient("ant-key-123", "claude-3-5-sonnet", httpClient: httpClient);

        var response = await client.GetResponseAsync(new[]
        {
            new ChatMessage(ChatRole.System, "You are an expert"),
            new ChatMessage(ChatRole.User, "Classify this email")
        });

        Assert.Equal("Anthropic response", response.Text);
        Assert.Equal(30, response.Usage?.InputTokenCount);
        Assert.Equal(12, response.Usage?.OutputTokenCount);

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.Headers.Contains("anthropic-version") || capturedRequest.Headers.Contains("x-api-key") || capturedRequest.Headers.Authorization != null);

        using var doc = JsonDocument.Parse(capturedBody!);
        var root = doc.RootElement;
        var systemElement = root.GetProperty("system");
        var systemText = systemElement.ValueKind == JsonValueKind.Array
            ? systemElement[0].GetProperty("text").GetString()
            : systemElement.GetString();
        Assert.Equal("You are an expert", systemText);
        Assert.Equal(1, root.GetProperty("messages").GetArrayLength());
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
