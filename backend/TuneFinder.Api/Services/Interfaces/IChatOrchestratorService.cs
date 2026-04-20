using TuneFinder.Api.Contracts;

namespace TuneFinder.Api.Services.Interfaces;

public interface IChatOrchestratorService
{
    Task<ChatResponse> HandleChatAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
