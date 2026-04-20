namespace TuneFinder.Api.Models.Llm;

public class LlmChatResult
{
    public string Content { get; set; } = string.Empty;
    public List<LlmToolCall> ToolCalls { get; set; } = [];
    public Dictionary<string, object?> AssistantMessage { get; set; } = [];
}
