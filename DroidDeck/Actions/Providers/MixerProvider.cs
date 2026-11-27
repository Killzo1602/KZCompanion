using System;
using System.Threading.Tasks;
using DroidDeck.Hubs;
using DroidDeck.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Actions.Providers
{
    public class MixerProvider : IActionProvider
    {
        private readonly MixerService _mixer;
        private readonly IAudioControlService _audio;
        private readonly IHubContext<DeckHub> _hub;
        private readonly ILogger<MixerProvider> _logger;

        public MixerProvider(MixerService mixer, IAudioControlService audio, IHubContext<DeckHub> hub, ILogger<MixerProvider> logger)
        {
            _mixer = mixer;
            _audio = audio;
            _hub = hub;
            _logger = logger;
        }

        public string Type => "mixer";

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Volume and mute",
            Icon = "volume_up",
            Category = "Media",
            Stateful = true,
            Editor = "mixer",
        };

        public async Task ExecuteAsync(ActionContext c)
        {
            var operation = c.Operation("togglemute");
            var processName = c.Get("processName");
            var deviceId = c.Get("deviceId");

            int? volume = c.Get("volume").Length > 0 ? Math.Clamp(c.Int("volume", 0), 0, 100) : null;
            var step = Math.Clamp(c.Int("step", 5), 1, 50);

            var isVolumeOp = operation is "setvolume" or "volumeup" or "volumedown";

            if (!string.IsNullOrWhiteSpace(processName))
            {
                if (isVolumeOp)
                {
                    _logger.LogWarning(
                        "Mixer: '{Op}' nao e suportado por processo ('{Proc}'). " +
                        "Use um dispositivo como alvo para controlar volume.", operation, processName);
                    return;
                }

                switch (operation)
                {
                    case "mute":
                        _audio.MuteByProcessName(processName, true);
                        break;
                    case "unmute":
                        _audio.MuteByProcessName(processName, false);
                        break;
                    default:
                        _audio.ToggleMuteByProcessName(processName);
                        break;
                }
                var muted = _audio.GetMuteStateByProcessName(processName);
                await _hub.Clients.All.SendAsync("ReceiveMuteState", new { processName, muted });
                _logger.LogInformation("Mixer: '{Op}' no processo '{Proc}' (muted={Muted})", operation, processName, muted);
                return;
            }

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                var isInput = string.Equals(c.Get("deviceKind"), "input", StringComparison.OrdinalIgnoreCase);

                var resolvedId = deviceId;
                if (string.Equals(deviceId, "default", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedId = _mixer.GetDefaultDeviceId(
                        isInput ? NAudio.CoreAudioApi.DataFlow.Capture : NAudio.CoreAudioApi.DataFlow.Render) ?? "";
                    if (string.IsNullOrEmpty(resolvedId))
                    {
                        _logger.LogWarning("Mixer: nao ha dispositivo padrao de {Kind}.", isInput ? "entrada" : "saida");
                        return;
                    }
                }

                var state = operation switch
                {
                    "mute" => _mixer.SetDeviceAudio(resolvedId, mute: true),
                    "unmute" => _mixer.SetDeviceAudio(resolvedId, mute: false),
                    "setvolume" => _mixer.SetDeviceAudio(resolvedId, volume: volume ?? 0),
                    "volumeup" => _mixer.SetDeviceAudio(resolvedId, delta: step),
                    "volumedown" => _mixer.SetDeviceAudio(resolvedId, delta: -step),
                    _ => _mixer.SetDeviceAudio(resolvedId, toggleMute: true),
                };

                if (state == null)
                {
                    _logger.LogWarning("Mixer: '{Op}' falhou no dispositivo '{Dev}'", operation, resolvedId);
                    return;
                }

                await _hub.Clients.All.SendAsync("ReceiveVolumeChange", new
                {
                    deviceId = state.Id,
                    type = isInput ? "input" : "output",
                    data = state
                });

                _logger.LogInformation("Mixer: '{Op}' em '{Title}' -> volume={Vol} mudo={Mute}",
                    operation, state.Title, state.Volume, state.Mute);
                return;
            }

            _logger.LogWarning("Mixer action ignorada: faltou 'processName' ou 'deviceId'.");
        }
    }
}
