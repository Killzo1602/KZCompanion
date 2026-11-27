using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DroidDeck.Lib;
using DroidDeck.Models;
using DroidDeck.Services;

namespace DroidDeck.Actions.Providers
{
    public class TimerProvider : IStatefulActionProvider
    {
        public const string Self = "self";

        private readonly TimerService _timers;
        private readonly StreamDeckConfigService _config;

        public TimerProvider(TimerService timers, StreamDeckConfigService config)
        {
            _timers = timers;
            _config = config;
        }

        public string Type => "timer";
        public string StateEvent => "ReceiveTimerState";
        public object GetState() => _timers.GetStatePayload();

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Timer, pomodoro and counter",
            Icon = "timer",
            Category = "Utilities",
            Stateful = true,
            Description = "The button shows the time live. When a countdown ends, the PC plays an alert and the phone vibrates.",
            Fields =
            {
                ActionField.Select("mode", "Type",
                    ("countdown", "Timer (countdown)"),
                    ("stopwatch", "Stopwatch"),
                    ("pomodoro", "Pomodoro (focus and break)"),
                    ("counter", "Counter")),
                ActionField.Number("durationMin", "Duration", 5, min: 0.1, unit: "min")
                    .When("mode", "countdown"),
                ActionField.Number("workMin", "Focus", 25, min: 1, max: 120, unit: " min")
                    .When("mode", "pomodoro"),
                ActionField.Number("breakMin", "Break", 5, min: 1, max: 60, unit: " min")
                    .When("mode", "pomodoro"),
                ActionField.Select("operation", "On tap",
                    ("toggle", "Start / pause (counter: add)"),
                    ("reset", "Reset"),
                    ("skip", "Skip to the next phase (pomodoro)"),
                    ("increment", "Add (counter)"),
                    ("decrement", "Subtract (counter)")),
                ActionField.Number("step", "Step", 1, min: 1, max: 100)
                    .When("mode", "counter"),
                new ActionField
                {
                    Key = "target",
                    Label = "Controlled timer",
                    Kind = FieldKind.Select,
                    Default = Self,
                    OptionsSource = "/api/actions/timer/targets",
                    Help = "For a separate \"reset\" button, pick another button's timer.",
                },
            },
        };

        public Task ExecuteAsync(ActionContext c)
        {
            var target = c.Get("target", Self);
            var ownerId = target == Self || target.Length == 0 ? c.ButtonId : target;
            if (string.IsNullOrEmpty(ownerId))
                ownerId = "avulso";

            var owner = ownerId == c.ButtonId ? c.Parameters : FindButtonAction(ownerId)?.Parameters ?? c.Parameters;
            var p = ToServiceParams(owner);
            if (c.Get("step").Length > 0) p["step"] = c.Get("step");

            _timers.Apply(ownerId, c.Operation("toggle"), p);
            return Task.CompletedTask;
        }

        private static Dictionary<string, string> ToServiceParams(IDictionary<string, string> source)
        {
            var p = new Dictionary<string, string>(source);
            foreach (var (from, to) in new[] { ("durationMin", "durationSec"), ("workMin", "workSec"), ("breakMin", "breakSec") })
            {
                if (source.TryGetValue(from, out var v) &&
                    double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var min) && min > 0)
                    p[to] = (min * 60).ToString(CultureInfo.InvariantCulture);
            }
            return p;
        }

        private DeckAction? FindButtonAction(string buttonId) =>
            _config.GetProfiles()
                .SelectMany(pr => pr.Buttons ?? new List<DeckButton>())
                .FirstOrDefault(b => b.Id == buttonId)?.Action;

        public List<FieldOption> Targets()
        {
            var options = new List<FieldOption> { new(Self, L.T("This button")) };
            foreach (var profile in _config.GetProfiles())
            {
                foreach (var b in profile.Buttons ?? new List<DeckButton>())
                {
                    if (b.Action?.Type != Type) continue;
                    if (b.Action.Parameters.TryGetValue("target", out var t) && t != Self && t.Length > 0) continue;
                    var mode = b.Action.Parameters.TryGetValue("mode", out var m) ? m : "countdown";
                    var name = string.IsNullOrWhiteSpace(b.Label) ? ModeName(mode) : b.Label;
                    options.Add(new FieldOption(b.Id, $"{profile.Name} › {name}"));
                }
            }
            return options;
        }

        private static string ModeName(string mode) => mode switch
        {
            "stopwatch" => "Stopwatch",
            "pomodoro" => "Pomodoro",
            "counter" => "Counter",
            _ => "Timer",
        };
    }
}
