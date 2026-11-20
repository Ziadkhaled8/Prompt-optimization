using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace PromptOptimizer.Llm.Strategies;

/// <summary>
/// General-purpose IChatClient strategy that connects to any OpenAI-compatible REST API.
/// Works out of the box with OpenAI, Groq, Ollama, DeepSeek, Together AI, Mistral, LocalAI, vLLM, etc.
/// </summary>
public sealed class GenericRestChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _defaultModel;
    private readonly string _chatEndpoint;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// When true, any system-role message is merged into the first user message as
    /// "[System]: {system}\n\n{user}".  Useful for classification-only endpoints that
    /// refuse a system role (e.g. Llama Prompt Guard, some HuggingFace models).
    /// </summary>
    public bool MergeSystemIntoUser { get; init; } = false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GenericRestChatClient(
        string apiKey,
        string defaultModel,
        string baseUrl = "https://api.openai.com/v1",
        string chatEndpoint = "chat/completions",
        HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? string.Empty;
        _defaultModel = defaultModel ?? throw new ArgumentNullException(nameof(defaultModel));
        _chatEndpoint = chatEndpoint.TrimStart('/');

        var normalizedBaseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";

        if (httpClient is null)
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(normalizedBaseUrl) };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            if (_httpClient.BaseAddress is null)
            {
                _httpClient.BaseAddress = new Uri(normalizedBaseUrl);
            }
            _ownsHttpClient = false;
        }
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messagesList = chatMessages.ToList();
        var model = options?.ModelId ?? _defaultModel;

        // First attempt: send as-is (or with system merged if already opted-in)
        var requestMessages = BuildMessages(messagesList, mergeSystem: MergeSystemIntoUser);

        var requestBody = new GenericChatRequest
        {
            Model = model,
            Messages = requestMessages,
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxOutputTokens
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        // If the endpoint rejects a system role, retry with system merged into user
        if (!response.IsSuccessStatusCode &&
            (int)response.StatusCode == 400 &&
            responseContent.Contains("single user message", StringComparison.OrdinalIgnoreCase) &&
            !MergeSystemIntoUser)
        {
            var retryMessages = BuildMessages(messagesList, mergeSystem: true);
            var retryBody = new GenericChatRequest
            {
                Model = model,
                Messages = retryMessages,
                Temperature = options?.Temperature,
                MaxTokens = options?.MaxOutputTokens
            };

            var retryJson = JsonSerializer.Serialize(retryBody, JsonOptions);
            using var retryRequest = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint)
            {
                Content = new StringContent(retryJson, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(_apiKey))
                retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var retryResponse = await _httpClient.SendAsync(retryRequest, cancellationToken);
            var retryContent = await retryResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!retryResponse.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"REST API error (status {(int)retryResponse.StatusCode}): {retryContent}",
                    null, retryResponse.StatusCode);

            responseContent = retryContent;
        }
        else if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"REST API error (status {(int)response.StatusCode}): {responseContent}",
                null,
                response.StatusCode);
        }

        var result = JsonSerializer.Deserialize<GenericChatResponse>(responseContent, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize chat response.");

        var choice = result.Choices?.FirstOrDefault();
        var content = choice?.Message?.Content ?? string.Empty;
        var responseMessage = new ChatMessage(ChatRole.Assistant, content);

        var chatResponse = new ChatResponse([responseMessage])
        {
            ModelId = result.Model ?? model,
            ResponseId = result.Id,
            Usage = new UsageDetails
            {
                InputTokenCount = result.Usage?.PromptTokens ?? 0,
                OutputTokenCount = result.Usage?.CompletionTokens ?? 0,
                TotalTokenCount = result.Usage?.TotalTokens ?? 0
            }
        };

        return chatResponse;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text)
        {
            ModelId = response.ModelId,
            ResponseId = response.ResponseId
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(GenericRestChatClient)) return this;
        return null;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static string MapRole(ChatRole role)
    {
        if (role == ChatRole.System) return "system";
        if (role == ChatRole.Assistant) return "assistant";
        return "user";
    }

    /// <summary>
    /// Builds the message list for the request.
    /// When <paramref name="mergeSystem"/> is true, any system message is prepended
    /// to the first user message as "[System]: {text}\n\n{userText}", and the system
    /// entry is dropped — satisfying models that only accept a single user message.
    /// </summary>
    private static List<GenericChatMessage> BuildMessages(
        List<ChatMessage> chatMessages, bool mergeSystem)
    {
        if (!mergeSystem)
        {
            return chatMessages.Select(m => new GenericChatMessage
            {
                Role = MapRole(m.Role),
                Content = m.Text ?? string.Empty
            }).ToList();
        }

        // Collect system text; skip system entries; prepend to first user message
        var systemParts = chatMessages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? string.Empty)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var nonSystem = chatMessages
            .Where(m => m.Role != ChatRole.System)
            .Select(m => new GenericChatMessage
            {
                Role = MapRole(m.Role),
                Content = m.Text ?? string.Empty
            })
            .ToList();

        if (systemParts.Count > 0 && nonSystem.Count > 0)
        {
            var prefix = string.Join("\n\n", systemParts);
            nonSystem[0] = new GenericChatMessage
            {
                Role = nonSystem[0].Role,
                Content = $"[System]: {prefix}\n\n{nonSystem[0].Content}"
            };
        }

        return nonSystem.Count > 0
            ? nonSystem
            : [new GenericChatMessage { Role = "user", Content = string.Join("\n\n", systemParts) }];
    }

    // ── Internal Request/Response DTOs ────────────────────────────────────────

    private sealed class GenericChatRequest
    {
        public required string Model { get; init; }
        public required List<GenericChatMessage> Messages { get; init; }
        public float? Temperature { get; init; }
        public int? MaxTokens { get; init; }
    }

    private sealed class GenericChatMessage
    {
        public required string Role { get; init; }
        public required string Content { get; init; }
    }

    private sealed class GenericChatResponse
    {
        public string? Id { get; init; }
        public string? Model { get; init; }
        public List<GenericChoice>? Choices { get; init; }
        public GenericUsage? Usage { get; init; }
    }

    private sealed class GenericChoice
    {
        public GenericMessageContent? Message { get; init; }
        public string? FinishReason { get; init; }
    }

    private sealed class GenericMessageContent
    {
        public string? Role { get; init; }
        public string? Content { get; init; }
    }

    private sealed class GenericUsage
    {
        public long PromptTokens { get; init; }
        public long CompletionTokens { get; init; }
        public long TotalTokens { get; init; }
    }
}
