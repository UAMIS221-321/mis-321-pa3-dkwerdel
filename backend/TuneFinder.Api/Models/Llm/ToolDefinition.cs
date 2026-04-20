namespace TuneFinder.Api.Models.Llm;

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object?> Parameters { get; set; } = [];
}
