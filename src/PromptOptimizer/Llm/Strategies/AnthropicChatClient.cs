using Anthropic;
using Microsoft.Extensions.AI;

namespace PromptOptimizer.Llm.Strategies;

/// <summary>
/// IChatClient strategy powered by the official Anthropic C# SDK (package 'Anthropic').
/// Delegates to AnthropicClient and its native AsIChatClient() adapter.
/// </summary>
public sealed class AnthropicChatClient : IChatClient
{
    private readonly AnthropicClient _anthropicClient;
    private readonly IChatClient _innerChatClient;

    public AnthropicChatClient(
        string apiKey,
        string defaultModel = "claude-3-5-sonnet-20241022",
        HttpClient? httpClient = null,
        string? baseUrl = null)
    {
        var effectiveBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.anthropic.com/v1"
            : baseUrl;

        _anthropicClient = httpClient is not null
            ? new AnthropicClient
            {
                ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey)),
                HttpClient = httpClient,
                BaseUrl = effectiveBaseUrl
            }
            : new AnthropicClient
            {
                ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey)),
                BaseUrl = effectiveBaseUrl
            };

        _innerChatClient = _anthropicClient.AsIChatClient(defaultModelId: defaultModel);
    }

    /// <summary>
    /// Gets the underlying Anthropic SDK client.
    /// </summary>
    public AnthropicClient SdkClient => _anthropicClient;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _innerChatClient.GetResponseAsync(chatMessages, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _innerChatClient.GetStreamingResponseAsync(chatMessages, options, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(AnthropicChatClient)) return this;
        if (serviceType == typeof(AnthropicClient)) return _anthropicClient;
        return _innerChatClient.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        _innerChatClient.Dispose();
    }
}
