using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Integrations
{
    public class MicHoldService
    {
        private sealed class Hold
        {
            public float Previous;
            public readonly HashSet<string> Owners = new();
        }

        private readonly IVoicemeeterApi _api;
        private readonly ILogger<MicHoldService> _logger;
        private readonly object _lock = new();
        private readonly Dictionary<string, Hold> _holds = new();

        public MicHoldService(IVoicemeeterApi api, ILogger<MicHoldService> logger)
        {
            _api = api;
            _logger = logger;
        }

        public void Down(string owner, string parameter)
        {
            lock (_lock)
            {
                try
                {
                    if (!_holds.TryGetValue(parameter, out var hold))
                    {
                        hold = new Hold { Previous = _api.Get(parameter) };
                        _api.Set(parameter, 1);
                        _holds[parameter] = hold;
                    }
                    hold.Owners.Add(owner);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Voicemeeter: corte não aplicado: {Error}", ex.Message);
                }
            }
        }

        public void Up(string owner, string parameter)
        {
            lock (_lock) Release(owner, parameter);
        }

        public void ReleaseOwner(string owner)
        {
            lock (_lock)
            {
                foreach (var parameter in _holds.Where(kv => kv.Value.Owners.Contains(owner)).Select(kv => kv.Key).ToList())
                    Release(owner, parameter);
            }
        }

        private void Release(string owner, string parameter)
        {
            if (!_holds.TryGetValue(parameter, out var hold) || !hold.Owners.Remove(owner) || hold.Owners.Count > 0) return;
            _holds.Remove(parameter);
            try { _api.Set(parameter, hold.Previous); }
            catch (Exception ex)
            {
                _logger.LogWarning("Voicemeeter: não voltou do corte: {Error}", ex.Message);
            }
        }
    }
}
