using System;
using System.Globalization;
using System.Threading.Tasks;
using DroidDeck.Actions;

namespace DroidDeck.Integrations
{
    public class VoicemeeterProvider : IActionProvider
    {
        public const string TypeName = "voicemeeter";

        public const float MinGain = -60, MaxGain = 12;

        private readonly IVoicemeeterApi _api;

        public VoicemeeterProvider(IVoicemeeterApi api) => _api = api;

        public string Type => TypeName;

        public const string HoldMute = "hold_mute";

        private static readonly string[] StripOps = { "toggle_mute", "mute", "unmute", HoldMute, "set_gain", "adjust_gain" };

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "Voicemeeter",
            Icon = "tune",
            Category = "Media",
            Description = "Mute and gain of Voicemeeter inputs and outputs, Macro Buttons and parameters.",
            Fields =
            {
                ActionField.Select("operation", "What to do",
                    ("toggle_mute", "Mute (toggle)"),
                    ("mute", "Mute"),
                    ("unmute", "Unmute"),
                    (HoldMute, "Cut while held (cough)"),
                    ("set_gain", "Set gain"),
                    ("adjust_gain", "Gain (+/-)"),
                    ("macro", "Press a Macro Buttons button"),
                    ("param_set", "Set a parameter"),
                    ("param_toggle", "Toggle a parameter (0/1)")),
                ActionField.Select("target", "Where",
                    ("strip", "Input (Strip)"),
                    ("bus", "Output (Bus)"))
                    .When("operation", StripOps),
                ActionField.Number("index", "Number", 1, 1, 8,
                    help: "Counting from the left, as in Voicemeeter (1 = the first).")
                    .When("operation", StripOps),
                ActionField.Number("gain", "Gain", 0, MinGain, MaxGain, "dB").When("operation", "set_gain"),
                ActionField.Number("step", "Step", 3, -24, 24, "dB").When("operation", "adjust_gain"),
                ActionField.Number("button", "Button number", 0, 0, 79,
                    help: "The button id in Macro Buttons (the first is 0).")
                    .When("operation", "macro"),
                ActionField.Text("parameter", "Parameter", "Strip[0].A1",
                    "Parameter name in the Voicemeeter Remote API.", required: true)
                    .When("operation", "param_set", "param_toggle"),
                ActionField.Number("value", "Value", 1).When("operation", "param_set"),
            },
        };

        public async Task ExecuteAsync(ActionContext ctx)
        {
            var op = ctx.Operation("toggle_mute");
            switch (op)
            {
                case HoldMute:
                    break;
                case "toggle_mute":
                case "mute":
                case "unmute":
                {
                    var p = Parameter(ctx.Get("target", "strip"), ctx.Int("index", 1), "Mute");
                    var mute = op == "mute" || (op == "toggle_mute" && _api.Get(p) < 0.5f);
                    _api.Set(p, mute ? 1 : 0);
                    break;
                }
                case "set_gain":
                    _api.Set(Parameter(ctx.Get("target", "strip"), ctx.Int("index", 1), "Gain"),
                        ClampGain((float)ctx.Number("gain", 0)));
                    break;
                case "adjust_gain":
                {
                    var p = Parameter(ctx.Get("target", "strip"), ctx.Int("index", 1), "Gain");
                    _api.Set(p, ClampGain(_api.Get(p) + (float)ctx.Number("step", 3)));
                    break;
                }
                case "macro":
                {
                    var button = ctx.Int("button", 0);
                    _api.SetMacroButton(button, 1);
                    await Task.Delay(50);
                    _api.SetMacroButton(button, 0);
                    break;
                }
                case "param_set":
                    _api.Set(CheckParameter(ctx.Get("parameter")), (float)ctx.Number("value", 1));
                    break;
                case "param_toggle":
                {
                    var p = CheckParameter(ctx.Get("parameter"));
                    _api.Set(p, _api.Get(p) >= 0.5f ? 0 : 1);
                    break;
                }
                default:
                    throw new InvalidOperationException("Unknown operation.");
            }
        }

        public static string? HoldMuteParameter(DroidDeck.Models.DeckAction action)
        {
            if (!string.Equals(action.Type, TypeName, StringComparison.OrdinalIgnoreCase) ||
                !action.Parameters.TryGetValue("operation", out var op) ||
                !string.Equals(op, HoldMute, StringComparison.OrdinalIgnoreCase))
                return null;
            action.Parameters.TryGetValue("target", out var target);
            action.Parameters.TryGetValue("index", out var index);
            var number = int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1;
            return Parameter(target ?? "strip", number, "Mute");
        }

        public static string Parameter(string target, int number, string name)
        {
            var kind = string.Equals(target, "bus", StringComparison.OrdinalIgnoreCase) ? "Bus" : "Strip";
            var index = Math.Clamp(number, 1, 8) - 1;
            return string.Create(CultureInfo.InvariantCulture, $"{kind}[{index}].{name}");
        }

        public static float ClampGain(float db) => Math.Clamp(db, MinGain, MaxGain);

        public static string CheckParameter(string parameter)
        {
            var p = parameter.Trim();
            if (p.Length == 0 || p.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(p, @"^[A-Za-z][A-Za-z0-9_\.\[\]]*$"))
                throw new InvalidOperationException("Unknown Voicemeeter parameter.");
            return p;
        }
    }
}
