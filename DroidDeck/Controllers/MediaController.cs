using DroidDeck.Services;
using DroidDeck.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace DroidDeck.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class MediaController : ControllerBase
    {
        private readonly MediaControlService _mediaService;
        private readonly ILogger<MediaController> _logger;
        private readonly IHubContext<DeckHub> _hubContext;

        public MediaController(
            MediaControlService mediaService,
            ILogger<MediaController> logger,
            IHubContext<DeckHub> hubContext)
        {
            _mediaService = mediaService;
            _logger = logger;
            _hubContext = hubContext;
        }

        [HttpGet("sessions")]
        public async Task<IActionResult> GetAllSessions()
        {
            _logger.LogDebug("GetAllSessions called");
            var sessions = await _mediaService.GetAllSessionsAsync();
            return Ok(sessions);
        }

        [HttpGet("sessions/{id}")]
        public async Task<IActionResult> GetSession(string id)
        {
            _logger.LogDebug("GetSession called for {id}", id);
            var session = await _mediaService.GetSessionByIdAsync(id);
            if (session != null)
                return Ok(session);
            return NotFound();
        }

        [HttpPost("sessions/{id}/command")]
        public async Task<IActionResult> SendCommand(string id, [FromBody] MediaCommandData command)
        {
            _logger.LogDebug("SendCommand called for {id}: {command}", id, command.Command);
            var result = await _mediaService.SendCommandAsync(id, command.Command);

            if (result.Success)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveMediaState", new
                {
                    sessionId = result.SessionId,
                    command = command.Command,
                    playing = result.Playing
                });

                if (result.Playing.HasValue)
                    await _hubContext.Clients.All.SendAsync("ReceiveMediaStatus",
                        new { playing = result.Playing.Value });

                return Ok(new { sessionId = result.SessionId, playing = result.Playing });
            }

            return BadRequest();
        }
    }

    public class MediaCommandData
    {
        public string Command { get; set; } = string.Empty;
    }
}
