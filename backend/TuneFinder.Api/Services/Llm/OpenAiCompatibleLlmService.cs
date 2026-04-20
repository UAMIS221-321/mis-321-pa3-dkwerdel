using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TuneFinder.Api.Models.Llm;
using TuneFinder.Api.Options;
using TuneFinder.Api.Services.Interfaces;

namespace TuneFinder.Api.Services.Llm;

public class OpenAiCompatibleLlmService : ILLMService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;

    public OpenAiCompatibleLlmService(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Ai:ApiKey is required.");
        }

        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<LlmChatResult> CreateChatCompletionAsync(
        List<Dictionary<string, object?>> messages,
        List<ToolDefinition> tools,
        CancellationToken cancellationToken = default
    )
    {
        var toolPayload = tools.Select(tool => new Dictionary<string, object?>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = tool.Parameters
            }
        }).ToList();

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.ChatModel,
            ["messages"] = messages,
            ["temperature"] = _options.Temperature,
            ["tools"] = toolPayload,
            ["tool_choice"] = "auto"
        };

        var requestContent = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync("chat/completions", requestContent, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LLM chat completion failed: {response.StatusCode} - {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var messageNode = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        var content = messageNode.TryGetProperty("content", out var contentNode)
            ? contentNode.GetString() ?? string.Empty
            : string.Empty;

        var toolCalls = new List<LlmToolCall>();
        if (messageNode.TryGetProperty("tool_calls", out var toolCallsNode) && toolCallsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolCallNode in toolCallsNode.EnumerateArray())
            {
                var functionNode = toolCallNode.GetProperty("function");
                toolCalls.Add(new LlmToolCall
                {
                    Id = toolCallNode.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                    Name = functionNode.GetProperty("name").GetString() ?? string.Empty,
                    ArgumentsJson = functionNode.GetProperty("arguments").GetString() ?? "{}"
                });
            }
        }

        var assistantMessage = new Dictionary<string, object?>
        {
            ["role"] = "assistant"
        };

        if (!string.IsNullOrWhiteSpace(content))
        {
            assistantMessage["content"] = content;
        }

        if (toolCalls.Count > 0)
        {
            assistantMessage["tool_calls"] = toolCalls.Select(tc => new Dictionary<string, object?>
            {
                ["id"] = tc.Id,
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = tc.Name,
                    ["arguments"] = tc.ArgumentsJson
                }
            }).ToList();
        }

        return new LlmChatResult
        {
            Content = content,
            ToolCalls = toolCalls,
            AssistantMessage = assistantMessage
        };
    }

    public async Task<List<float>> CreateEmbeddingAsync(string input, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.EmbeddingModel,
            ["input"] = input
        };

        var requestContent = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync("embeddings", requestContent, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Embedding request failed: {response.StatusCode} - {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var embeddingArray = document.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(x => x.GetSingle())
            .ToList();

        return embeddingArray;
    }
}
