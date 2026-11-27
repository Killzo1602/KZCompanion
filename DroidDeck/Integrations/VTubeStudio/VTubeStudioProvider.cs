using System;
using System.Threading.Tasks;
using DroidDeck.Actions;

namespace DroidDeck.Integrations
{
    public class VTubeStudioProvider : IStatefulActionProvider
    {
        public const string TypeName = "vtubestudio";

        private readonly VTubeStudioIntegration _vts;

        public VTubeStudioProvider(VTubeStudioIntegration vts) => _vts = vts;

        public string Type => TypeName;
        public string StateEvent => VTubeStudioIntegration.Event;
        public object GetState() => _vts.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "VTube Studio",
            Icon = "face",
            Category = "Streaming",
            Description = "Triggers the model's hotkeys (expressions, animations) in VTube Studio or switches models.",
            Settings = "vtubestudio",
            StateEvent = StateEvent,
            Fields =
            {
                ActionField.Select("operation", "What to do",
                    ("hotkey", "Trigger hotkey"),
                    ("model", "Load model")),
                ActionField.Dynamic("hotkey", "Hotkey", "/api/integrations/vtubestudio/options/hotkeys",
                    "Hotkeys of the currently loaded model. Also accepts a typed name.", allowCustom: true)
                    .When("operation", "hotkey"),
                ActionField.Dynamic("model", "Model", "/api/integrations/vtubestudio/options/models")
                    .When("operation", "model"),
            },
        };

        public Task ExecuteAsync(ActionContext ctx)
        {
            if (ctx.Operation("hotkey") == "model")
            {
                var model = ctx.Get("model");
                if (model.Trim().Length == 0) throw new InvalidOperationException("Choose a model.");
                return _vts.LoadModelAsync(model);
            }
            var hotkey = ctx.Get("hotkey");
            if (hotkey.Trim().Length == 0) throw new InvalidOperationException("Choose a hotkey.");
            return _vts.TriggerHotkeyAsync(hotkey);
        }
    }
}
