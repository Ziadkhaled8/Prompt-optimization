using Microsoft.Extensions.AI;
using PromptOptimizer.Llm.Strategies;

namespace PromptOptimizer.Llm;

/// <summary>
/// Supported provider strategy types.
/// </summary>
public enum LlmProviderType
{
    Anthropic,
    GenericRest,
    Gemini,
    Mock
}

/// <summary>
/// Strategy factory for creating IChatClient implementations based on provider configuration.
/// Allows seamless switching between Anthropic, Google Gemini (free tier), generic OpenAI-compatible REST endpoints, and Mock mode.
/// </summary>
public static class LlmClientFactory
{
    /// <summary>
    /// Creates an IChatClient strategy instance matching the specified provider type.
    /// </summary>
    public static IChatClient Create(
        LlmProviderType providerType,
        LlmProviderConfig config,
        HttpClient? httpClient = null,
        string? chatEndpoint = null)
    {
        return providerType switch
        {
            LlmProviderType.Anthropic => new AnthropicChatClient(
                apiKey: config.ApiKey,
                defaultModel: config.ModelId,
                httpClient: httpClient,
                baseUrl: config.BaseUrl),

            LlmProviderType.Gemini => new GenericRestChatClient(
                apiKey: config.ApiKey,
                defaultModel: config.ModelId,
                baseUrl: config.BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta/openai/",
                chatEndpoint: chatEndpoint ?? "chat/completions",
                httpClient: httpClient),

            LlmProviderType.GenericRest => new GenericRestChatClient(
                apiKey: config.ApiKey,
                defaultModel: config.ModelId,
                baseUrl: config.BaseUrl ?? "https://api.openai.com/v1",
                chatEndpoint: chatEndpoint ?? "chat/completions",
                httpClient: httpClient),

            LlmProviderType.Mock => new MockChatClient(),

            _ => throw new ArgumentOutOfRangeException(
                nameof(providerType),
                $"Unsupported provider type: {providerType}")
        };
    }

    /// <summary>
    /// Parses a string into an LlmProviderType enum.
    /// </summary>
    public static LlmProviderType ParseProviderType(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return LlmProviderType.Anthropic;

        if (Enum.TryParse<LlmProviderType>(providerName, ignoreCase: true, out var result))
            return result;

        if (providerName.Equals("Gemini", StringComparison.OrdinalIgnoreCase) ||
            providerName.Equals("Google", StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderType.Gemini;
        }

        if (providerName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) ||
            providerName.Equals("Ollama", StringComparison.OrdinalIgnoreCase) ||
            providerName.Equals("Groq", StringComparison.OrdinalIgnoreCase) ||
            providerName.Equals("DeepSeek", StringComparison.OrdinalIgnoreCase) ||
            providerName.Equals("REST", StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderType.GenericRest;
        }

        throw new ArgumentException(
            $"Unknown provider type: '{providerName}'. Expected Gemini, Anthropic, GenericRest, OpenAI, Ollama, Groq, or Mock.");
    }
}
