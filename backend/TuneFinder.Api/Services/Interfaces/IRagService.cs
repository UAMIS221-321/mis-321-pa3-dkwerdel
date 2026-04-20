namespace TuneFinder.Api.Services.Interfaces;

public interface IRagService
{
    bool ShouldUseRag(string userMessage);
    Task<List<string>> RetrieveRelevantChunksAsync(string userMessage, int topN = 4);
}
