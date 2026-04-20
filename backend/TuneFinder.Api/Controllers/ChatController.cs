using Microsoft.AspNetCore.Mvc;
using TuneFinder.Api.Contracts;
using TuneFinder.Api.Services.Interfaces;

namespace TuneFinder.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatOrchestratorService _chatOrchestratorService;

    public ChatController(IChatOrchestratorService chatOrchestratorService)
    {
        _chatOrchestratorService = chatOrchestratorService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PostChat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "sessionId and message are required." });
        }

        try
        {
            var response = await _chatOrchestratorService.HandleChatAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Chat request failed: {ex.Message}" });
        }
    }
}
