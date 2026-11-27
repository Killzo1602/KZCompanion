using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Actions;
using DroidDeck.Hubs;
using DroidDeck.Integrations;
using DroidDeck.Lib;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace DroidDeck.Services
{
    public record TallyTarget(string Id, string Source, string Label, string State)
    {
        public const string Program = "program", Preview = "preview", Idle = "idle";
    }

    public class TallyService : BackgroundService
    {
        public const string Event = "ReceiveTallyState";
        public const string ManualId = "manual/onair";

        private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan VoicemeeterEvery = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan VoicemeeterRetry = TimeSpan.FromSeconds(5);
        private const int MaxStrips = 8;

        private readonly ObsService _obs;
        private readonly VmixIntegration _vmix;
        private readonly MairlistIntegration _mairlist;
        private readonly RadioDjIntegration _radioDj;
        private readonly IVoicemeeterApi _voicemeeter;
        private readonly IHubContext<DeckHub> _hub;

        private volatile bool _manual;
        private volatile bool[] _stripsOpen = Array.Empty<bool>();
        private DateTime _nextVoicemeeter;
        private string _lastKey = "";

        private readonly Dictionary<string, string> _known = new();

        public event Action<TallyTarget, string>? TargetChanged;

        public Func<bool>? WatchRequested { get; set; }

        public TallyService(ObsService obs, VmixIntegration vmix, MairlistIntegration mairlist,
            RadioDjIntegration radioDj, IVoicemeeterApi voicemeeter, IHubContext<DeckHub> hub)
        {
            _obs = obs;
            _vmix = vmix;
            _mairlist = mairlist;
            _radioDj = radioDj;
            _voicemeeter = voicemeeter;
            _hub = hub;
        }

        public bool Manual => _manual;

        public void SetManual(bool on)
        {
            _manual = on;
            _lastKey = "";
        }

        public List<TallyTarget> Targets()
        {
            var list = new List<TallyTarget>
            {
                new(ManualId, "manual", L.T("Manual signal (ON AIR)"), _manual ? TallyTarget.Program : TallyTarget.Idle),
            };
            if (_obs.Connected)
                list.AddRange(ObsTargets(_obs.Scenes, _obs.CurrentScene, _obs.PreviewScene, _obs.Streaming, _obs.Recording));
            if (_vmix.Snapshot is { } vmix)
                list.AddRange(VmixTargets(vmix, _vmix.TallyOf));
            if (_mairlist.OnAir is { } onAir)
                list.Add(new("mairlist/onair", "mairlist", "mAirList: " + L.T("Live"), onAir ? TallyTarget.Program : TallyTarget.Idle));
            if (_radioDj.Input is { } input)
                list.Add(new("radiodj/input", "radiodj", "RadioDJ: " + L.T("Live input"), input ? TallyTarget.Program : TallyTarget.Idle));
            var strips = _stripsOpen;
            for (var i = 0; i < strips.Length; i++)
                list.Add(new($"voicemeeter/strip:{i + 1}", "voicemeeter", $"Voicemeeter: {L.T("Open microphone")} {i + 1}",
                    strips[i] ? TallyTarget.Program : TallyTarget.Idle));
            return list;
        }

        public string? StateOf(string id)
        {
            if (id.StartsWith("voicemeeter/", StringComparison.Ordinal))
                _stripsOpen = ReadStrips(_voicemeeter);
            return Targets().FirstOrDefault(t => t.Id == id)?.State;
        }

        public List<(TallyTarget target, string previous)> DetectChanges(IEnumerable<TallyTarget> targets)
        {
            var changes = new List<(TallyTarget, string)>();
            foreach (var t in targets)
            {
                if (_known.TryGetValue(t.Id, out var previous) && previous != t.State)
                    changes.Add((t, previous));
                _known[t.Id] = t.State;
            }
            return changes;
        }

        public static IEnumerable<TallyTarget> ObsTargets(IEnumerable<string> scenes, string? program, string? preview, bool streaming, bool recording)
        {
            yield return new("obs/stream", "obs", "OBS: " + L.T("Streaming"), streaming ? TallyTarget.Program : TallyTarget.Idle);
            yield return new("obs/record", "obs", "OBS: " + L.T("Recording"), recording ? TallyTarget.Program : TallyTarget.Idle);
            foreach (var scene in scenes)
                yield return new("obs/scene:" + scene, "obs", "OBS: " + scene,
                    scene == program ? TallyTarget.Program : scene == preview ? TallyTarget.Preview : TallyTarget.Idle);
        }

        public static IEnumerable<TallyTarget> VmixTargets(VmixSnapshot vmix, Func<int, int> tallyOf)
        {
            yield return new("vmix/stream", "vmix", "vMix: " + L.T("Streaming"), vmix.Streaming ? TallyTarget.Program : TallyTarget.Idle);
            yield return new("vmix/record", "vmix", "vMix: " + L.T("Recording"), vmix.Recording ? TallyTarget.Program : TallyTarget.Idle);
            foreach (var input in vmix.Inputs)
                yield return new("vmix/input:" + input.Key, "vmix", $"vMix: {input.Number}. {input.Title}",
                    tallyOf(input.Number) switch
                    {
                        VmixIntegration.TallyProgram => TallyTarget.Program,
                        VmixIntegration.TallyPreview => TallyTarget.Preview,
                        _ => TallyTarget.Idle,
                    });
        }

        public object GetState() => new
        {
            manual = _manual,
            targets = Targets().ToDictionary(t => t.Id, t => new { label = t.Label, state = t.State, source = t.Source }),
        };

        public List<FieldOption> Options() =>
            Targets().Select(t => new FieldOption(t.Id, t.Label)).ToList();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (DeckHub.ConnectedCount > 0 || WatchRequested?.Invoke() == true)
                    {
                        PollVoicemeeter();
                        var targets = Targets();
                        foreach (var (target, previous) in DetectChanges(targets))
                            TargetChanged?.Invoke(target, previous);
                        var key = string.Join("|", targets.Select(t => t.Id + "=" + t.State));
                        if (key != _lastKey)
                        {
                            _lastKey = key;
                            await _hub.SendLocalizedAsync(Event, GetState);
                        }
                    }
                    else
                    {
                        _known.Clear();
                    }
                }
                catch (Exception) when (!stoppingToken.IsCancellationRequested) { }
                try { await Task.Delay(Tick, stoppingToken); }
                catch (TaskCanceledException) { }
            }
        }

        private void PollVoicemeeter()
        {
            var now = DateTime.UtcNow;
            if (now < _nextVoicemeeter) return;
            var open = ReadStrips(_voicemeeter);
            _stripsOpen = open;
            _nextVoicemeeter = now + (open.Length > 0 ? VoicemeeterEvery : VoicemeeterRetry);
        }

        public static bool[] ReadStrips(IVoicemeeterApi api)
        {
            var open = new List<bool>();
            try
            {
                for (var i = 1; i <= MaxStrips; i++)
                    open.Add(api.Get(VoicemeeterProvider.Parameter("strip", i, "Mute")) < 0.5f);
            }
            catch (Exception)
            {
            }
            return open.ToArray();
        }
    }

    public class TallyProvider : IStatefulActionProvider
    {
        public const string TypeName = "tally";

        private readonly TallyService _tally;

        public TallyProvider(TallyService tally) => _tally = tally;

        public string Type => TypeName;
        public string StateEvent => TallyService.Event;
        public object GetState() => _tally.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "Tally and ON AIR",
            Icon = "sensors",
            Category = "Studio",
            Stateful = true,
            StateEvent = StateEvent,
            Description = "Full-screen tally or ON AIR, or the state of a scene, input or microphone on the button.",
            Fields =
            {
                ActionField.Select("operation", "What to do",
                    ("open", "Open the tally screen"),
                    ("show", "Only show the state on the button"),
                    ("manual_toggle", "Manual ON AIR signal (toggle)"),
                    ("manual_on", "Turn the manual signal on"),
                    ("manual_off", "Turn the manual signal off")),
                ActionField.Dynamic("target", "What to follow", "/api/tally/targets",
                    "The list only shows what is connected now. With the manual signal and \"only show\", tapping the button turns it on and off.",
                    allowCustom: true)
                    .When("operation", "open", "show"),
            },
        };

        public Task ExecuteAsync(ActionContext c)
        {
            switch (c.Operation("open"))
            {
                case "manual_toggle":
                    _tally.SetManual(!_tally.Manual);
                    break;
                case "show" when c.Get("target") == TallyService.ManualId:
                    _tally.SetManual(!_tally.Manual);
                    break;
                case "manual_on":
                    _tally.SetManual(true);
                    break;
                case "manual_off":
                    _tally.SetManual(false);
                    break;
            }
            return Task.CompletedTask;
        }
    }
}
