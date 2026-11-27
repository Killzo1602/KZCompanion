using System.Threading.Tasks;
using DroidDeck.Hubs;
using DroidDeck.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Actions.Providers
{
    public class MediaProvider : IActionProvider
    {
        private readonly MediaControlService _media;
        private readonly IHubContext<DeckHub> _hub;
        private readonly ILogger<MediaProvider> _logger;

        public MediaProvider(MediaControlService media, IHubContext<DeckHub> hub, ILogger<MediaProvider> logger)
        {
            _media = media;
            _hub = hub;
            _logger = logger;
        }

        public string Type => "media";

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Media",
            Icon = "play",
            Category = "Media",
            Fields =
            {
                ActionField.Select("command", "Command",
                    ("playpause", "Play / Pause"),
                    ("next", "Next track"),
                    ("previous", "Previous track"),
                    ("stop", "Stop"),
                    ("play", "Play"),
                    ("pause", "Pause")),
            },
        };

        public async Task ExecuteAsync(ActionContext c)
        {
            var command = c.Get("command");
            if (command.Length == 0) return;

            var sessionId = c.Get("sessionId") is { Length: > 0 } s ? s : null;

            var result = await _media.SendCommandAsync(sessionId, command);
            if (!result.Success)
            {
                _logger.LogWarning("Midia: comando '{Command}' do deck nao foi aplicado.", command);
                return;
            }

            if (result.Playing.HasValue)
                await _hub.Clients.All.SendAsync("ReceiveMediaStatus", new { playing = result.Playing.Value });
        }
    }
}
