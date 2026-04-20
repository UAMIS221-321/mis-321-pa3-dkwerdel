namespace TuneFinder.Api.Contracts;

public class ChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
