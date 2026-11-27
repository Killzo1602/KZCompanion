using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DroidDeck.Actions;

namespace DroidDeck.Integrations
{
    public class VmixProvider : IStatefulActionProvider
    {
        public const string TypeName = "vmix";

        private static readonly string[] InputOps = { "preview", "program", "quickplay", "overlay", "toggle_audio", "set_volume" };

        private readonly VmixIntegration _vmix;

        public VmixProvider(VmixIntegration vmix) => _vmix = vmix;

        public string Type => TypeName;
        public string StateEvent => VmixIntegration.Event;
        public object GetState() => _vmix.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "vMix",
            Icon = "live_tv",
            Category = "Studio",
            Stateful = true,
            Settings = "vmix",
            StateEvent = StateEvent,
            Description = "Cut, transition, inputs, overlays, audio, recording and streaming, with vMix state on the button.",
            Fields =
            {
                ActionField.Select("operation", "What to do",
                    ("cut", "Cut (preview to program)"),
                    ("fade", "Fade transition"),
                    ("preview", "Send input to preview"),
                    ("program", "Send input to program"),
                    ("quickplay", "Input Quick Play"),
                    ("overlay", "Overlay (toggle)"),
                    ("overlay_off", "Remove all overlays"),
                    ("toggle_audio", "Input audio (toggle)"),
                    ("set_volume", "Input volume"),
                    ("master_audio", "Master audio (toggle)"),
                    ("master_volume", "Master volume"),
                    ("toggle_record", "Record (toggle)"),
                    ("toggle_stream", "Stream (toggle)"),
                    ("toggle_external", "External output (toggle)"),
                    ("toggle_multicorder", "MultiCorder (toggle)"),
                    ("fade_to_black", "Fade to Black (toggle)"),
                    ("snapshot", "Screenshot"),
                    ("custom", "Other function")),
                ActionField.Dynamic("input", "Input", "/api/integrations/vmix/options/inputs",
                    "Also accepts the number or name of the input.", allowCustom: true)
                    .When("operation", InputOps),
                ActionField.Number("duration", "Duration", 1000, 0, 60000, "ms").When("operation", "fade"),
                ActionField.Number("overlay", "Overlay", 1, 1, 8).When("operation", "overlay"),
                ActionField.Number("volume", "Volume", 100, 0, 100, "%").When("operation", "set_volume", "master_volume"),
                ActionField.Text("function", "Function", "SetText",
                    "Function name in the vMix shortcut reference.", required: true)
                    .When("operation", "custom"),
                ActionField.Text("query", "Parameters (optional)", "Input=1&SelectedName=Titulo&Value=Oi",
                    "As in the vMix Web API, with values in URL format.")
                    .When("operation", "custom"),
            },
        };

        public Task ExecuteAsync(ActionContext ctx)
        {
            var (function, query) = Build(ctx);
            return _vmix.FunctionAsync(function, query);
        }

        public static (string function, string query) Build(ActionContext c)
        {
            switch (c.Operation("cut"))
            {
                case "cut": return ("Cut", "");
                case "fade": return ("Fade", Query(("Duration", Whole(c, "duration", 1000, 0, 60000))));
                case "preview": return ("PreviewInput", InputQuery(c));
                case "program": return ("ActiveInput", InputQuery(c));
                case "quickplay": return ("QuickPlay", InputQuery(c));
                case "overlay": return ("OverlayInput" + Whole(c, "overlay", 1, 1, 8), InputQuery(c));
                case "overlay_off": return ("OverlayInputAllOff", "");
                case "toggle_audio": return ("Audio", InputQuery(c));
                case "set_volume":
                    return ("SetVolume", InputQuery(c) + "&" + Query(("Value", Whole(c, "volume", 100, 0, 100))));
                case "master_audio": return ("MasterAudio", "");
                case "master_volume": return ("SetMasterVolume", Query(("Value", Whole(c, "volume", 100, 0, 100))));
                case "toggle_record": return ("StartStopRecording", "");
                case "toggle_stream": return ("StartStopStreaming", "");
                case "toggle_external": return ("StartStopExternal", "");
                case "toggle_multicorder": return ("StartStopMultiCorder", "");
                case "fade_to_black": return ("FadeToBlack", "");
                case "snapshot": return ("Snapshot", "");
                case "custom": return (CheckFunction(c.Get("function")), CheckQuery(c.Get("query")));
                default: throw new InvalidOperationException("Unknown operation.");
            }
        }

        private static string InputQuery(ActionContext c)
        {
            var input = c.Get("input").Trim();
            if (input.Length == 0) throw new InvalidOperationException("Escolha uma entrada do vMix.");
            return Query(("Input", input));
        }

        private static string Whole(ActionContext c, string key, int fallback, int min, int max) =>
            Math.Clamp(c.Int(key, fallback), min, max).ToString(CultureInfo.InvariantCulture);

        private static string Query(params (string key, string value)[] items)
        {
            var parts = new string[items.Length];
            for (var i = 0; i < items.Length; i++)
                parts[i] = items[i].key + "=" + Uri.EscapeDataString(items[i].value);
            return string.Join("&", parts);
        }

        public static string CheckFunction(string function)
        {
            var f = function.Trim();
            if (!Regex.IsMatch(f, "^[A-Za-z0-9]{1,64}$"))
                throw new InvalidOperationException("Função do vMix inválida.");
            return f;
        }

        public static string CheckQuery(string query)
        {
            var q = query.Trim();
            foreach (var ch in q)
                if (char.IsControl(ch)) throw new InvalidOperationException("Parâmetros do vMix inválidos.");
            return q;
        }
    }
}
