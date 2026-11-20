using Microsoft.Extensions.AI;

namespace PromptOptimizer.Llm;

/// <summary>
/// Configuration for a specific LLM provider.
/// </summary>
public sealed class LlmProviderConfig
{
    /// <summary>Display name (e.g. "Claude 3.5 Sonnet", "GPT-4o-mini").</summary>
    public required string Name { get; init; }

    /// <summary>The model ID to pass to the API (e.g. "claude-sonnet-4-20250514").</summary>
    public required string ModelId { get; init; }

    /// <summary>API key (loaded from config/env).</summary>
    public required string ApiKey { get; init; }

    /// <summary>Base URL for the API endpoint (if applicable).</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Cost per 1K input tokens in dollars.</summary>
    public decimal InputCostPer1K { get; init; }

    /// <summary>Cost per 1K output tokens in dollars.</summary>
    public decimal OutputCostPer1K { get; init; }

    /// <summary>Maximum retry attempts on transient failures.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Timeout per request.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Which provider strategy to use (Anthropic, GenericRest, Mock).</summary>
    public LlmProviderType ProviderType { get; init; } = LlmProviderType.Anthropic;

    /// <summary>Custom chat completion endpoint path (default "chat/completions" for GenericRest).</summary>
    public string? ChatEndpoint { get; init; }
}

/// <summary>
/// Wraps an IChatClient with retry logic, timeout handling, and cost tracking.
/// Provider-agnostic: the actual IChatClient implementation comes from the DI container.
/// </summary>
public sealed class ProviderClient
{
    private readonly IChatClient _client;
    private readonly LlmProviderConfig _config;

    private long _totalInputTokens;
    private long _totalOutputTokens;
    private decimal _totalCost;

    public ProviderClient(IChatClient client, LlmProviderConfig config)
    {
        _client = client;
        _config = config;
    }

    public string ModelId => _config.ModelId;
    public long TotalInputTokens => _totalInputTokens;
    public long TotalOutputTokens => _totalOutputTokens;
    public decimal TotalCost => _totalCost;

    /// <summary>
    /// Sends a chat request with retry logic and cost tracking.
    /// </summary>
    public async Task<LlmResponse> ChatAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userMessage)
        };

        Exception? lastException = null;

        for (int attempt = 0; attempt <= _config.MaxRetries; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_config.Timeout);

                var response = await _client.GetResponseAsync(messages, cancellationToken: cts.Token);

                var text = response.Text ?? string.Empty;

                // Track token usage
                var usage = response.Usage;
                var inputTokens = usage?.InputTokenCount ?? 0;
                var outputTokens = usage?.OutputTokenCount ?? 0;
                var cost = (inputTokens / 1000m * _config.InputCostPer1K)
                         + (outputTokens / 1000m * _config.OutputCostPer1K);

                Interlocked.Add(ref _totalInputTokens, inputTokens);
                Interlocked.Add(ref _totalOutputTokens, outputTokens);
                // Decimal doesn't support Interlocked, use lock-free pattern
                lock (this) { _totalCost += cost; }

                return new LlmResponse
                {
                    Text = text,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    EstimatedCost = cost
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastException = new TimeoutException(
                    $"Request timed out after {_config.Timeout.TotalSeconds}s (attempt {attempt + 1}/{_config.MaxRetries + 1})");
            }
            catch (Exception ex) when (attempt < _config.MaxRetries)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }

        throw new InvalidOperationException(
            $"All {_config.MaxRetries + 1} attempts failed for {_config.Name}",
            lastException);
    }

    /// <summary>
    /// Sends a chat request and parses the JSON response.
    /// Retries with a correction message if JSON parsing fails.
    /// </summary>
    public async Task<T> ChatJsonAsync<T>(
        string systemPrompt,
        string userMessage,
        int maxJsonRetries = 2,
        CancellationToken ct = default)
    {
        var response = await ChatAsync(systemPrompt, userMessage, ct);
        var parseResult = JsonResponseParser.TryParse<T>(response.Text);

        if (parseResult.Success)
            return parseResult.Value!;

        // Log the raw failing response for diagnostics
        Console.Error.WriteLine(
            $"[JSON retry] Attempt 1 failed ({parseResult.Error}). Raw response ({response.Text.Length} chars):\n" +
            $"{response.Text[..Math.Min(300, response.Text.Length)]}…");

        // Retry with correction
        for (int i = 0; i < maxJsonRetries; i++)
        {
            var correctionMessage =
                $"Your previous response could not be parsed as JSON.\n" +
                $"Error: {parseResult.Error}\n\n" +
                $"Rules:\n" +
                $"• Respond with ONLY the raw JSON — no markdown fences, no ```json, no prose\n" +
                $"• No <think> or reasoning blocks\n" +
                $"• All string values must be on one line (escape newlines as \\n)\n" +
                $"• Start your response with {{ or [ and end with }} or ]\n\n" +
                $"Previous response (first 500 chars):\n" +
                $"{response.Text[..Math.Min(500, response.Text.Length)]}";

            response = await ChatAsync(systemPrompt, correctionMessage, ct);
            parseResult = JsonResponseParser.TryParse<T>(response.Text);

            if (parseResult.Success)
                return parseResult.Value!;

            Console.Error.WriteLine(
                $"[JSON retry] Attempt {i + 2} failed ({parseResult.Error}). Raw:\n" +
                $"{response.Text[..Math.Min(300, response.Text.Length)]}…");
        }

        throw new InvalidOperationException(
            $"Failed to get valid JSON after {maxJsonRetries + 1} attempts. Last error: {parseResult.Error}");
    }
}

/// <summary>
/// Response from an LLM call with metadata.
/// </summary>
public sealed class LlmResponse
{
    public required string Text { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal EstimatedCost { get; init; }
}
