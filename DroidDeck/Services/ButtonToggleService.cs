using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DroidDeck.Hubs;
using DroidDeck.Lib;
using DroidDeck.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Services
{
    public class ButtonToggleService
    {
        public const string Event = "ReceiveButtonToggles";

        private readonly ILogger<ButtonToggleService> _logger;
        private readonly IHubContext<DeckHub>? _hub;
        private readonly object _lock = new();
        private readonly Dictionary<string, bool> _states;

        public ButtonToggleService(ILogger<ButtonToggleService> logger, IHubContext<DeckHub>? hub = null)
        {
            _logger = logger;
            _hub = hub;
            _states = Load();
        }

        private static string FilePath => Path.Combine(AppPaths.RoamingDir, "toggles.json");

        public bool IsOn(string buttonId)
        {
            lock (_lock) return _states.TryGetValue(buttonId, out var on) && on;
        }

        public object GetState()
        {
            lock (_lock) return _states.Where(kv => kv.Value).ToDictionary(kv => kv.Key, _ => true);
        }

        public DeckAction? Press(DeckButton button)
        {
            bool wasOn;
            lock (_lock)
            {
                wasOn = _states.TryGetValue(button.Id, out var on) && on;
                if (wasOn) _states.Remove(button.Id);
                else _states[button.Id] = true;
                Save();
            }
            _ = BroadcastAsync();
            return wasOn && button.ActiveAction != null ? button.ActiveAction : button.Action;
        }

        public void Set(string buttonId, bool on)
        {
            lock (_lock)
            {
                if (on) _states[buttonId] = true;
                else _states.Remove(buttonId);
                Save();
            }
            _ = BroadcastAsync();
        }

        private async Task BroadcastAsync()
        {
            if (_hub == null) return;
            try { await _hub.Clients.All.SendAsync(Event, GetState()); }
            catch { }
        }

        private Dictionary<string, bool> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "toggles.json ilegivel, botoes voltam desligados");
            }
            return new();
        }

        private void Save()
        {
            try
            {
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_states));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao gravar toggles.json");
            }
        }
    }
}
