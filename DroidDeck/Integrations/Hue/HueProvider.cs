using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DroidDeck.Actions;

namespace DroidDeck.Integrations
{
    public class HueProvider : IStatefulActionProvider
    {
        public const string TypeName = "hue";

        private readonly HueIntegration _hue;

        public HueProvider(HueIntegration hue) => _hue = hue;

        public string Type => TypeName;
        public string StateEvent => HueIntegration.Event;
        public object GetState() => _hue.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "Philips Hue",
            Icon = "emoji_objects",
            Category = "Home",
            Description = "Philips Hue lights, rooms and scenes through the Hue Bridge on your network, no cloud account.",
            Settings = "hue",
            StateEvent = StateEvent,
            Fields =
            {
                ActionField.Select("operation", "What to do",
                    ("toggle", "Toggle on/off"),
                    ("on", "Turn on"),
                    ("off", "Turn off"),
                    ("brightness", "Set brightness"),
                    ("brightness_step", "Brightness (+/-)"),
                    ("scene", "Activate scene")),
                ActionField.Select("target", "Control",
                    ("light", "One light"),
                    ("group", "A room or zone"))
                    .When("operation", "toggle", "on", "off", "brightness", "brightness_step"),
                ActionField.Dynamic("id", "Light or room", "/api/integrations/hue/options/items?target={target}")
                    .When("operation", "toggle", "on", "off", "brightness", "brightness_step"),
                ActionField.Dynamic("scene", "Scene", "/api/integrations/hue/options/scenes").When("operation", "scene"),
                ActionField.Number("brightness", "Brightness", 100, 1, 100, "%").When("operation", "brightness"),
                ActionField.Number("step", "Brightness step", 10, -100, 100, "%").When("operation", "brightness_step"),
            },
        };

        public async Task ExecuteAsync(ActionContext ctx)
        {
            var op = ctx.Operation("toggle");
            if (op == "scene")
            {
                var scene = ctx.Get("scene").Trim();
                if (scene.Length == 0) throw new InvalidOperationException("Choose a scene.");
                await _hue.CallAsync(HttpMethod.Put, "groups/0/action", new JsonObject { ["scene"] = scene });
                return;
            }

            var group = ctx.Get("target", "light") == "group";
            var id = ctx.Get("id").Trim();
            if (id.Length == 0) throw new InvalidOperationException("Choose a light or room.");
            var path = group ? $"groups/{Uri.EscapeDataString(id)}" : $"lights/{Uri.EscapeDataString(id)}";

            bool? isOn = null;
            if (op == "toggle")
            {
                var current = await _hue.CallAsync(HttpMethod.Get, path);
                isOn = IsOn(current, group);
            }
            var body = BuildState(op, isOn, ctx.Int("brightness", 100), ctx.Int("step", 10));
            await _hue.CallAsync(HttpMethod.Put, path + (group ? "/action" : "/state"), body);
        }

        public static bool IsOn(JsonNode? node, bool group) =>
            (group ? node?["state"]?["any_on"] : node?["state"]?["on"])?.GetValue<bool>() == true;

        public static JsonObject BuildState(string op, bool? isOn, int brightness, int step) => op switch
        {
            "on" => new JsonObject { ["on"] = true },
            "off" => new JsonObject { ["on"] = false },
            "toggle" => new JsonObject { ["on"] = isOn != true },
            "brightness" => new JsonObject { ["on"] = true, ["bri"] = ToHue(brightness) },
            "brightness_step" => step > 0
                ? new JsonObject { ["on"] = true, ["bri_inc"] = Math.Clamp((int)Math.Round(step * 254 / 100.0), -254, 254) }
                : new JsonObject { ["bri_inc"] = Math.Clamp((int)Math.Round(step * 254 / 100.0), -254, 254) },
            _ => throw new InvalidOperationException("Unknown operation."),
        };

        public static int ToHue(int percent) => Math.Clamp((int)Math.Round(Math.Clamp(percent, 1, 100) * 254 / 100.0), 1, 254);
    }
}
