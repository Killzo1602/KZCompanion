using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroidDeck.Controllers
{
    [Route("api")]
    [ApiController]
    [Authorize]
    public class DevController : ControllerBase
    {
        private readonly DevStatusService _dev;
        public DevController(DevStatusService dev) => _dev = dev;

        [HttpGet("ai-sessions")]
        public IActionResult Sessions() => Ok(new { sessions = DevStatusService.ClaudeSessions(AiUsageService.ClaudeDir) });

        [HttpGet("dev/github")]
        public async Task<IActionResult> GitHub(CancellationToken ct) => Ok(await _dev.GitHubAsync(ct));

        [HttpGet("dev/docker")]
        public async Task<IActionResult> Docker(CancellationToken ct) => Ok(await _dev.DockerAsync(ct));

        [HttpPost("dev/docker/{id}/{operation}")]
        public async Task<IActionResult> DockerCommand(string id, string operation)
        {
            var (found, error) = await _dev.DockerActionAsync(id, operation, CancellationToken.None);
            if (!found) return NotFound(new { error = "That container is no longer there." });
            return error == null ? Ok(new { ok = true }) : BadRequest(new { error });
        }
    }
}
