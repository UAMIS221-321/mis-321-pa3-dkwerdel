using TuneFinder.Api.Models.Llm;

namespace TuneFinder.Api.Services.Interfaces;

public interface IToolService
{
    List<ToolDefinition> GetToolDefinitions();
    Task<string> ExecuteToolAsync(string toolName, string argumentsJson);
}
