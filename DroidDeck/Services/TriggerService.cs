using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Lib;
using DroidDeck.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Services
{
    public class TriggerService : BackgroundService
    {
        public class Trigger
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string? Name { get; set; }
            public bool Enabled { get; set; } = true;
            public string Event { get; set; } = "schedule";
            public string? Time { get; set; }
            public string? Days { get; set; }
            public string? App { get; set; }

            public string? Target { get; set; }
            public string? TargetLabel { get; set; }
            public string? ProfileId { get; set; }
            public string? ButtonId { get; set; }
            public StepCondition? If { get; set; }
        }

        public static readonly string[] Events = { "schedule", "app_start", "app_exit", "resume", "startup", "tally_on", "tally_off" };

        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        private readonly StreamDeckConfigService _config;
        private readonly ButtonToggleService _toggles;
        private readonly ActionExecutorService _executor;
        private readonly Conditions _conditions;
        private readonly ILogger<TriggerService> _logger;
        private readonly object _lock = new();
        private List<Trigger> _triggers;

        public Func<DateTime> Now { get; set; } = () => DateTime.Now;

        public Func<HashSet<string>> RunningProcesses { get; set; } = DefaultRunningProcesses;

        public TriggerService(StreamDeckConfigService config, ButtonToggleService toggles,
            ActionExecutorService executor, Conditions conditions, ILogger<TriggerService> logger,
            TallyService? tally = null)
        {
            _config = config;
            _toggles = toggles;
            _executor = executor;
            _conditions = conditions;
            _logger = logger;
            _triggers = Load();
            if (tally != null)
            {
                tally.WatchRequested = () => GetAll().Any(t => t.Enabled && t.Event is "tally_on" or "tally_off");
                tally.TargetChanged += (target, previous) => _ = SafeTallyChangedAsync(target.Id, previous, target.State);
            }
        }

        private async Task SafeTallyChangedAsync(string id, string previous, string current)
        {
            try { await OnTallyChangedAsync(id, previous, current); }
            catch (Exception ex) { _logger.LogWarning("Gatilhos: {Message}", ex.Message); }
        }

        public async Task OnTallyChangedAsync(string id, string previous, string current)
        {
            var wasOn = previous == TallyTarget.Program;
            var isOn = current == TallyTarget.Program;
            if (wasOn == isOn) return;
            var eventName = isOn ? "tally_on" : "tally_off";
            await FireAsync(t => t.Event == eventName && t.Target == id);
        }

        private static string FilePath => Path.Combine(AppPaths.RoamingDir, "triggers.json");

        public List<Trigger> GetAll()
        {
            lock (_lock) return _triggers.ToList();
        }

        public void SaveAll(List<Trigger> triggers)
        {
            foreach (var t in triggers)
            {
                if (string.IsNullOrWhiteSpace(t.Id)) t.Id = Guid.NewGuid().ToString("N");
                if (!Events.Contains(t.Event)) t.Event = "schedule";
            }
            lock (_lock) _triggers = triggers.ToList();
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(triggers, Json)); }
            catch (Exception ex) { _logger.LogWarning("Gatilhos: nao deu para salvar ({Message})", ex.Message); }
        }

        private static List<Trigger> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<List<Trigger>>(File.ReadAllText(FilePath), Json) ?? new();
            }
            catch { }
            return new();
        }

        private HashSet<string>? _lastProcesses;
        private DateTime _lastTick;
        private string? _lastMinute;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) { return; }
            await FireAsync(t => t.Event == "startup");

            _lastTick = Now();
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await TickAsync(); }
                catch (Exception ex) { _logger.LogDebug("Gatilhos: {Message}", ex.Message); }
                try { await Task.Delay(Tick, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        public async Task TickAsync()
        {
            var now = Now();

            if (_lastTick != default && now - _lastTick > TimeSpan.FromSeconds(30))
                await FireAsync(t => t.Event == "resume");
            _lastTick = now;

            var minute = now.ToString("HH:mm");
            if (minute != _lastMinute)
            {
                _lastMinute = minute;
                await FireAsync(t => t.Event == "schedule" && t.Time?.Trim() == minute &&
                                     (string.IsNullOrWhiteSpace(t.Days) || Conditions.Weekday(t.Days, now)));
            }

            if (GetAll().Any(t => t.Enabled && t.Event is "app_start" or "app_exit"))
            {
                var current = RunningProcesses();
                if (_lastProcesses != null)
                {
                    var started = current.Except(_lastProcesses).ToHashSet();
                    var exited = _lastProcesses.Except(current).ToHashSet();
                    if (started.Count > 0)
                        await FireAsync(t => t.Event == "app_start" && started.Contains(Normalize(t.App)));
                    if (exited.Count > 0)
                        await FireAsync(t => t.Event == "app_exit" && exited.Contains(Normalize(t.App)));
                }
                _lastProcesses = current;
            }
            else
            {
                _lastProcesses = null;
            }
        }

        private async Task FireAsync(Func<Trigger, bool> match)
        {
            foreach (var t in GetAll().Where(t => t.Enabled && match(t)))
            {
                if (!_conditions.Evaluate(t.If)) continue;
                _logger.LogInformation("Gatilho '{Name}' ({Event})", t.Name ?? t.Id, t.Event);
                await RunAsync(t);
            }
        }

        public async Task<bool> RunAsync(Trigger t)
        {
            var button = _config.GetProfile(t.ProfileId ?? "")?.Buttons?.Find(b => b.Id == t.ButtonId);
            if (button == null)
            {
                _logger.LogWarning("Gatilho '{Name}': o botao nao existe mais", t.Name ?? t.Id);
                return false;
            }
            var action = button.Toggle ? _toggles.Press(button) : button.Action;
            if (action != null) await _executor.ExecuteActionAsync(action, button.Id);
            return true;
        }

        private static string Normalize(string? app)
        {
            app = (app ?? "").Trim().ToLowerInvariant();
            return app.EndsWith(".exe") ? app[..^4] : app;
        }

        private static HashSet<string> DefaultRunningProcesses()
        {
            var set = new HashSet<string>();
            foreach (var p in Process.GetProcesses())
            {
                try { set.Add(p.ProcessName.ToLowerInvariant()); }
                catch { }
                finally { p.Dispose(); }
            }
            return set;
        }
    }
}
