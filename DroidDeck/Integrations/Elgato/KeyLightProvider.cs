using System;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Actions;

namespace DroidDeck.Integrations
{
    public class KeyLightProvider : IActionProvider
    {
        public const string TypeName = "keylight";
        public const int DefaultPort = 9123;

        public const int MinKelvin = 2900, MaxKelvin = 7000;

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private readonly IHttpClientFactory _http;

        public KeyLightProvider(IHttpClientFactory http) => _http = http;

        public string Type => TypeName;

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "Elgato Key Light",
            Icon = "wb_incandescent",
            Category = "Home",
            Description = "On, off, brightness and color temperature of Elgato lights (Key Light, Light Strip) over the local network.",
            Fields =
            {
                ActionField.Text("host", "Light IP", "192.168.0.50",
                    "Shown in Elgato Control Center, in the light's settings. Different port: 192.168.0.50:9123.", required: true),
                ActionField.Select("operation", "What to do",
                    ("toggle", "Toggle on/off"),
                    ("on", "Turn on"),
                    ("off", "Turn off"),
                    ("brightness", "Set brightness"),
                    ("brightness_step", "Brightness (+/-)"),
                    ("temperature", "Set temperature"),
                    ("temperature_step", "Temperature (+/-)")),
                ActionField.Number("brightness", "Brightness", 50, 1, 100, "%").When("operation", "brightness"),
                ActionField.Number("step", "Brightness step", 10, -100, 100, "%").When("operation", "brightness_step"),
                ActionField.Number("kelvin", "Temperature", 4500, MinKelvin, MaxKelvin, "K").When("operation", "temperature"),
                ActionField.Number("kelvinStep", "Temperature step", 300, -4000, 4000, "K",
                    "Positive makes the light cooler (bluer).").When("operation", "temperature_step"),
            },
        };

        public async Task ExecuteAsync(ActionContext ctx)
        {
            var url = LightsUrl(ctx.Get("host"));
            var op = ctx.Operation("toggle");
            var client = _http.CreateClient();
            client.Timeout = Timeout;

            JsonObject? current = null;
            if (op is "toggle" or "brightness_step" or "temperature_step")
                current = JsonNode.Parse(await client.GetStringAsync(url)) as JsonObject;

            var body = BuildPayload(op, current, ctx.Int("brightness", 50), ctx.Int("step", 10),
                ctx.Int("kelvin", 4500), ctx.Int("kelvinStep", 300));
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await client.PutAsync(url, content);
            response.EnsureSuccessStatusCode();
        }

        public static Uri LightsUrl(string host)
        {
            var h = host.Trim();
            if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) h = h[7..];
            h = h.TrimEnd('/');
            if (h.Length == 0 || !Uri.TryCreate("http://" + h, UriKind.Absolute, out var parsed) || parsed.PathAndQuery != "/")
                throw new InvalidOperationException("Invalid light IP.");
            var port = parsed.IsDefaultPort && !h.EndsWith(":80") ? DefaultPort : parsed.Port;
            return new UriBuilder("http", parsed.Host, port, "/elgato/lights").Uri;
        }

        public static JsonObject BuildPayload(string op, JsonObject? current, int brightness, int step, int kelvin, int kelvinStep)
        {
            var lights = current?["lights"] as JsonArray;
            var count = Math.Max(lights?.Count ?? 1, 1);
            var array = new JsonArray();
            for (var i = 0; i < count; i++)
            {
                var now = lights?[i] as JsonObject;
                var light = new JsonObject();
                switch (op)
                {
                    case "on":
                        light["on"] = 1;
                        break;
                    case "off":
                        light["on"] = 0;
                        break;
                    case "toggle":
                        light["on"] = Int(now, "on", 0) == 1 ? 0 : 1;
                        break;
                    case "brightness":
                        light["on"] = 1;
                        light["brightness"] = Math.Clamp(brightness, 1, 100);
                        break;
                    case "brightness_step":
                        light["on"] = 1;
                        light["brightness"] = Math.Clamp(Int(now, "brightness", 50) + step, 1, 100);
                        break;
                    case "temperature":
                        light["temperature"] = ToElgato(kelvin);
                        break;
                    case "temperature_step":
                        light["temperature"] = ToElgato(ToKelvin(Int(now, "temperature", ToElgato(4500))) + kelvinStep);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown operation.");
                }
                array.Add(light);
            }
            return new JsonObject { ["numberOfLights"] = count, ["lights"] = array };
        }

        private static int Int(JsonObject? o, string key, int fallback) =>
            o?[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : fallback;

        public static int ToElgato(int kelvin) =>
            Math.Clamp((int)Math.Round(1_000_000.0 / Math.Clamp(kelvin, MinKelvin, MaxKelvin)), 143, 344);

        public static int ToKelvin(int elgato) =>
            (int)Math.Round(1_000_000.0 / Math.Clamp(elgato, 143, 344));
    }
}
