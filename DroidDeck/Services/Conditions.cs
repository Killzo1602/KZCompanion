using System;
using System.Diagnostics;
using System.Globalization;
using DroidDeck.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DroidDeck.Services
{
    public class Conditions
    {
        public static readonly string[] Checks =
        {
            "obs_streaming", "obs_recording", "discord_mute", "discord_deaf",
            "toggle_on", "app_running", "app_focused", "time_between", "weekday",
            "tally_program", "tally_preview",
        };

        private readonly IServiceProvider _services;

        public Func<DateTime> Now { get; set; } = () => DateTime.Now;

        public Conditions(IServiceProvider services) => _services = services;

        public bool Evaluate(StepCondition? condition)
        {
            if (condition == null || string.IsNullOrWhiteSpace(condition.Check)) return true;
            var result = Check(condition.Check.Trim().ToLowerInvariant(), condition.Value?.Trim() ?? "");
            return condition.Negate ? !result : result;
        }

        private bool Check(string check, string value)
        {
            switch (check)
            {
                case "obs_streaming": return _services.GetService<ObsService>()?.Streaming == true;
                case "obs_recording": return _services.GetService<ObsService>()?.Recording == true;
                case "discord_mute": return _services.GetService<DiscordRpcService>()?.SelfMute == true;
                case "discord_deaf": return _services.GetService<DiscordRpcService>()?.SelfDeaf == true;
                case "toggle_on": return _services.GetService<ButtonToggleService>()?.IsOn(value) == true;
                case "app_running": return IsRunning(value);
                case "app_focused":
                    return string.Equals(ForegroundWatcher.ForegroundProcess(), StripExe(value), StringComparison.OrdinalIgnoreCase);
                case "time_between": return TimeBetween(value, Now());
                case "weekday": return Weekday(value, Now());
                case "tally_program": return _services.GetService<TallyService>()?.StateOf(value) == TallyTarget.Program;
                case "tally_preview": return _services.GetService<TallyService>()?.StateOf(value) == TallyTarget.Preview;
                default: return true;
            }
        }

        private static string StripExe(string app) =>
            app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? app[..^4] : app;

        private static bool IsRunning(string app)
        {
            app = StripExe(app);
            if (app.Length == 0) return false;
            var procs = Process.GetProcessesByName(app);
            foreach (var p in procs) p.Dispose();
            return procs.Length > 0;
        }

        public static bool TimeBetween(string range, DateTime now)
        {
            var parts = range.Split('-', 2);
            if (parts.Length != 2 ||
                !TimeSpan.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, out var from) ||
                !TimeSpan.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, out var to))
                return true;
            var t = now.TimeOfDay;
            return from <= to ? t >= from && t < to : t >= from || t < to;
        }

        public static bool Weekday(string days, DateTime now)
        {
            var today = (int)now.DayOfWeek;
            foreach (var item in days.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var range = item.Split('-', 2);
                if (!int.TryParse(range[0], out var a)) continue;
                var b = range.Length == 2 && int.TryParse(range[1], out var end) ? end : a;
                if (a <= b ? today >= a && today <= b : today >= a || today <= b) return true;
            }
            return false;
        }
    }
}
