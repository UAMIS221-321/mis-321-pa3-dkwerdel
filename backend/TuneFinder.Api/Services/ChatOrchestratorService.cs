using MySqlConnector;
using TuneFinder.Api.Contracts;
using TuneFinder.Api.Services.Interfaces;

namespace TuneFinder.Api.Services;

public class ChatOrchestratorService : IChatOrchestratorService
{
    private readonly ILLMService _llmService;
    private readonly IRagService _ragService;
    private readonly IToolService _toolService;
    private readonly string _connectionString;

    public ChatOrchestratorService(
        ILLMService llmService,
        IRagService ragService,
        IToolService toolService,
        string connectionString
    )
    {
        _llmService = llmService;
        _ragService = ragService;
        _toolService = toolService;
        _connectionString = connectionString;
    }

    public async Task<ChatResponse> HandleChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = request.SessionId.Trim();
        var normalizedMessage = request.Message.Trim();

        await SaveChatMessageAsync(normalizedSessionId, "user", normalizedMessage, cancellationToken);

        var usedRag = _ragService.ShouldUseRag(normalizedMessage);
        var retrievedContext = usedRag
            ? await _ragService.RetrieveRelevantChunksAsync(normalizedMessage, 4)
            : [];

        var messageLog = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["role"] = "system",
                ["content"] =
                    "You are TuneFinder AI, a concise, friendly music discovery assistant. " +
                    "Give specific recommendations and short rationale. If a tool can answer better, call it. " +
                    "Do not use Markdown formatting like **bold** or _italics_."
            }
        };

        if (retrievedContext.Count > 0)
        {
            messageLog.Add(new Dictionary<string, object?>
            {
                ["role"] = "system",
                ["content"] = "Relevant music knowledge context:\n" + string.Join("\n---\n", retrievedContext)
            });
        }

        var recentMessages = await GetRecentMessagesAsync(normalizedSessionId, 8, cancellationToken);
        foreach (var item in recentMessages)
        {
            messageLog.Add(new Dictionary<string, object?>
            {
                ["role"] = item.Role,
                ["content"] = item.MessageText
            });
        }

        messageLog.Add(new Dictionary<string, object?>
        {
            ["role"] = "user",
            ["content"] = normalizedMessage
        });

        var toolsUsed = new List<string>();
        var tools = _toolService.GetToolDefinitions();

        var llmResult = await _llmService.CreateChatCompletionAsync(messageLog, tools, cancellationToken);

        var toolLoopCount = 0;
        while (llmResult.ToolCalls.Count > 0 && toolLoopCount < 3)
        {
            toolLoopCount++;
            messageLog.Add(llmResult.AssistantMessage);

            foreach (var toolCall in llmResult.ToolCalls)
            {
                if (!toolsUsed.Contains(toolCall.Name))
                {
                    toolsUsed.Add(toolCall.Name);
                }

                var toolResult = await _toolService.ExecuteToolAsync(toolCall.Name, toolCall.ArgumentsJson);
                messageLog.Add(new Dictionary<string, object?>
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = toolCall.Id,
                    ["content"] = toolResult
                });
            }

            llmResult = await _llmService.CreateChatCompletionAsync(messageLog, tools, cancellationToken);
        }

        var assistantMessage = string.IsNullOrWhiteSpace(llmResult.Content)
            ? "I could not generate a response this time. Please try again."
            : NormalizeAssistantText(llmResult.Content);

        await SaveChatMessageAsync(normalizedSessionId, "assistant", assistantMessage, cancellationToken);

        return new ChatResponse
        {
            SessionId = normalizedSessionId,
            Response = assistantMessage,
            UsedRag = usedRag && retrievedContext.Count > 0,
            ToolsUsed = toolsUsed,
            RetrievedContext = retrievedContext
        };
    }

    private async Task SaveChatMessageAsync(
        string sessionId,
        string role,
        string messageText,
        CancellationToken cancellationToken
    )
    {
        const string sql = @"
INSERT INTO chat_messages (session_id, role, message_text, created_at)
VALUES (@sessionId, @role, @messageText, UTC_TIMESTAMP());";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@sessionId", sessionId);
        command.Parameters.AddWithValue("@role", role);
        command.Parameters.AddWithValue("@messageText", messageText);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizeAssistantText(string text)
    {
        return text
            .Trim()
            .Replace("**", string.Empty)
            .Replace("__", string.Empty);
    }

    private async Task<List<ChatMessageDto>> GetRecentMessagesAsync(
        string sessionId,
        int limit,
        CancellationToken cancellationToken
    )
    {
        const string sql = @"
SELECT role, message_text, created_at
FROM (
    SELECT role, message_text, created_at
    FROM chat_messages
    WHERE session_id = @sessionId
    ORDER BY created_at DESC
    LIMIT @limit
) recent
ORDER BY created_at ASC;";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@sessionId", sessionId);
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<ChatMessageDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ChatMessageDto
            {
                Role = reader.GetString("role"),
                MessageText = reader.GetString("message_text"),
                CreatedAtUtc = reader.GetDateTime("created_at")
            });
        }

        return results;
    }
}
