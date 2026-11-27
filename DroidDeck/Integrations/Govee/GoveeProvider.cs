using System;
using System.Threading.Tasks;
using DroidDeck.Actions;

namespace DroidDeck.Integrations
{
    public class GoveeProvider : IActionProvider
    {
        public const string TypeName = "govee";

        private readonly GoveeLan _lan;

        public GoveeProvider(GoveeLan lan) => _lan = lan;

        public string Type => TypeName;

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "Govee",
            Icon = "light_mode",
            Category = "Home",
            Description = "Govee bulbs and strips over the local network (needs LAN Control enabled in the Govee Home app).",
            Fields =
            {
                ActionField.Dynamic("ip", "Device", "/api/govee/devices",
                    "Searches the network when opened. If it doesn't show up, type the device IP.", allowCustom: true),
                ActionField.Select("operation", "What to do",
                    ("toggle", "Toggle on/off"),
                    ("on", "Turn on"),
                    ("off", "Turn off"),
                    ("brightness", "Set brightness"),
                    ("color", "Set color"),
                    ("temperature", "Set temperature (white)")),
                ActionField.Number("brightness", "Brightness", 100, 1, 100, "%").When("operation", "brightness"),
                ActionField.Text("color", "Color", "#FF8000", "In #RRGGBB format.").WithDefault("#FF8000").When("operation", "color"),
                ActionField.Number("kelvin", "Temperature", 4000, 2000, 9000, "K").When("operation", "temperature"),
            },
        };

        public async Task ExecuteAsync(ActionContext ctx)
        {
            var ip = ctx.Get("ip").Trim();
            switch (ctx.Operation("toggle"))
            {
                case "on":
                    await _lan.SendAsync(ip, GoveeLan.Turn(true));
                    break;
                case "off":
                    await _lan.SendAsync(ip, GoveeLan.Turn(false));
                    break;
                case "toggle":
                    var on = await _lan.IsOnAsync(ip, TimeSpan.FromSeconds(2))
                        ?? throw new InvalidOperationException("The Govee device didn't respond. Check the IP and LAN Control in the Govee Home app.");
                    await _lan.SendAsync(ip, GoveeLan.Turn(!on));
                    break;
                case "brightness":
                    await _lan.SendAsync(ip, GoveeLan.Brightness(ctx.Int("brightness", 100)));
                    break;
                case "color":
                    var (r, g, b) = GoveeLan.ParseHex(ctx.Get("color", "#FF8000"));
                    await _lan.SendAsync(ip, GoveeLan.Color(r, g, b));
                    break;
                case "temperature":
                    await _lan.SendAsync(ip, GoveeLan.Color(0, 0, 0, ctx.Int("kelvin", 4000)));
                    break;
                default:
                    throw new InvalidOperationException("Unknown operation.");
            }
        }
    }
}
