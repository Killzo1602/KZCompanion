using System.Threading.Tasks;
using DroidDeck.Services;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Actions.Providers
{
    public class TuyaProvider : IStatefulActionProvider
    {
        private readonly TuyaService _tuya;
        private readonly ILogger<TuyaProvider> _logger;

        public TuyaProvider(TuyaService tuya, ILogger<TuyaProvider> logger)
        {
            _tuya = tuya;
            _logger = logger;
        }

        public string Type => "tuya";

        public string StateEvent => "ReceiveTuyaState";
        public object GetState() => _tuya.GetStatePayload();

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Smart home (Tuya)",
            Icon = "lightbulb",
            Category = "Home",
            Stateful = true,
            Editor = "tuya",
            Settings = "tuya",
        };

        public async Task ExecuteAsync(ActionContext c)
        {
            var deviceId = c.Get("deviceId");
            var code = c.Get("code");
            if (deviceId.Length == 0 || code.Length == 0)
            {
                _logger.LogWarning("Tuya: acao sem deviceId/code");
                return;
            }

            switch (c.Operation("toggle"))
            {
                case "toggle":
                    await _tuya.ToggleAsync(deviceId, code);
                    break;
                case "set":
                    await _tuya.SendCommandAsync(deviceId, code, ParseValue(c.Get("value"), c.Get("valueType")));
                    break;
                default:
                    _logger.LogWarning("Tuya: operacao desconhecida {Op}", c.Get("operation"));
                    break;
            }
        }

        private static object? ParseValue(string raw, string valueType)
        {
            switch ((valueType ?? "").Trim().ToLowerInvariant())
            {
                case "boolean":
                    return bool.TryParse(raw, out var b) && b;
                case "integer":
                    return long.TryParse(raw, out var l) ? l : 0L;
                default:
                    return raw;
            }
        }
    }
}
