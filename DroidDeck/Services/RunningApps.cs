using System;
using System.Collections.Generic;
using System.Linq;

namespace DroidDeck.Services
{
    public sealed record RawWindow(
        long Hwnd,
        uint FramePid,
        uint Pid,
        string Title,
        bool Minimized,
        bool Foreground,
        string? ExePath,
        string ProcessName,
        bool Elevated);

    public sealed record AppWindow(string Id, long Hwnd, uint FramePid, string Title, bool Minimized, bool Foreground);

    public sealed class RunningApp
    {
        public string Id { get; init; } = "";
        public string Name { get; set; } = "";
        public string ProcessName { get; init; } = "";
        public string? ExePath { get; init; }
        public IReadOnlyList<uint> Pids { get; init; } = Array.Empty<uint>();
        public IReadOnlyList<AppWindow> Windows { get; init; } = Array.Empty<AppWindow>();
        public bool Foreground => Windows.Any(w => w.Foreground);
        public bool Minimized => Windows.Count > 0 && Windows.All(w => w.Minimized);
        public bool Elevated { get; init; }

        public AppWindow? Main => Windows.FirstOrDefault();
    }

    public static class RunningAppsGrouper
    {
        public const int MaxWindowsPerApp = 12;

        public static string KeyFor(string? exePath, uint pid) =>
            exePath != null ? AudioMixerService.ShortHash(exePath.ToLowerInvariant()) : "pid" + pid;

        public static string WindowId(long hwnd, uint framePid) => $"{hwnd:x}.{framePid}";

        public static bool TryParseWindowId(string? id, out long hwnd, out uint framePid)
        {
            hwnd = 0;
            framePid = 0;
            if (string.IsNullOrEmpty(id)) return false;
            var dot = id.IndexOf('.');
            if (dot <= 0 || dot == id.Length - 1) return false;
            return long.TryParse(id[..dot], System.Globalization.NumberStyles.HexNumber, null, out hwnd)
                && uint.TryParse(id[(dot + 1)..], out framePid);
        }

        public static List<RunningApp> Group(IEnumerable<RawWindow> windows, Func<string?, string, string> nameFor)
        {
            var groups = new Dictionary<string, List<RawWindow>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var w in windows)
            {
                var key = KeyFor(w.ExePath, w.Pid);
                if (!groups.TryGetValue(key, out var list))
                {
                    groups[key] = list = new List<RawWindow>();
                    order.Add(key);
                }
                if (list.Any(x => x.Hwnd == w.Hwnd)) continue;
                list.Add(w);
            }

            var apps = new List<RunningApp>();
            foreach (var key in order)
            {
                var list = groups[key]
                    .OrderByDescending(w => w.Foreground)
                    .ThenBy(w => w.Minimized)
                    .ToList();
                var first = list[0];
                apps.Add(new RunningApp
                {
                    Id = key,
                    Name = nameFor(first.ExePath, first.ProcessName),
                    ProcessName = first.ProcessName,
                    ExePath = first.ExePath,
                    Pids = list.Select(w => w.Pid).Distinct().OrderBy(p => p).ToList(),
                    Windows = list.Take(MaxWindowsPerApp)
                        .Select(w => new AppWindow(WindowId(w.Hwnd, w.FramePid), w.Hwnd, w.FramePid, w.Title, w.Minimized, w.Foreground))
                        .ToList(),
                    Elevated = list.Any(w => w.Elevated),
                });
            }
            return apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
        }

        private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
        {
            "TextInputHost", "SearchHost", "SearchApp", "StartMenuExperienceHost", "ShellExperienceHost",
            "LockApp", "SystemSettingsBroker", "ShellHost", "dwm", "csrss", "winlogon", "fontdrvhost",
            "ctfmon", "sihost", "taskhostw", "RuntimeBroker", "WidgetService", "Widgets",
        };

        private static readonly HashSet<string> HiddenClasses = new(StringComparer.Ordinal)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
            "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "Windows.UI.Core.CoreWindow",
        };

        public static bool IsOwnerOf(string? parentPath, string? childPath, string windowsDir)
        {
            if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(childPath)) return false;
            if (string.Equals(parentPath, childPath, StringComparison.OrdinalIgnoreCase)) return false;
            if (IsUnder(parentPath, windowsDir)) return false;
            var parentDir = System.IO.Path.GetDirectoryName(parentPath);
            var childDir = System.IO.Path.GetDirectoryName(childPath);
            if (string.IsNullOrEmpty(parentDir) || string.IsNullOrEmpty(childDir)) return false;
            if (System.IO.Path.GetDirectoryName(parentDir) == null) return false;
            return IsUnder(childDir + "\\x", parentDir);
        }

        private static bool IsUnder(string path, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            var d = dir.TrimEnd('\\') + "\\";
            return path.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUserFacing(string processName, string windowClass)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            if (Hidden.Contains(processName)) return false;
            if (HiddenClasses.Contains(windowClass)) return false;
            if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                return windowClass == "CabinetWClass";
            return true;
        }
    }
}
