namespace TuneFinder.Api.Contracts;

public class ChatResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public bool UsedRag { get; set; }
    public List<string> ToolsUsed { get; set; } = [];
    public List<string> RetrievedContext { get; set; } = [];
}
