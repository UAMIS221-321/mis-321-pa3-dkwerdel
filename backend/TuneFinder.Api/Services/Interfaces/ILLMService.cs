using TuneFinder.Api.Models.Llm;

namespace TuneFinder.Api.Services.Interfaces;

public interface ILLMService
{
    Task<LlmChatResult> CreateChatCompletionAsync(
        List<Dictionary<string, object?>> messages,
        List<ToolDefinition> tools,
        CancellationToken cancellationToken = default
    );

    Task<List<float>> CreateEmbeddingAsync(string input, CancellationToken cancellationToken = default);
}
