using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;
using DroidDeck.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Services
{
    public enum AppActionOutcome
    {
        Ok,
        AppGone,
        WindowGone,
        Elevated,
        FocusDenied,
        StillOpen,
        Failed,
    }

    public sealed record AppActionResult(AppActionOutcome Outcome, string? Detail = null)
    {
        public bool Ok => Outcome == AppActionOutcome.Ok;
    }

    public sealed class RunningAppsService : IDisposable
    {
        public const string Event = "ReceiveRunningApps";

        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

        private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(10);

        private readonly IHubContext<DeckHub>? _hub;
        private readonly ILogger? _logger;
        private readonly object _gate = new();
        private readonly HashSet<string> _watchers = new();
        private readonly ConcurrentDictionary<(uint Pid, long Started), ProcInfo> _procs = new();
        private readonly ConcurrentDictionary<string, string> _names = new();
        private readonly ConcurrentDictionary<string, byte[]?> _icons = new();
        private WinEventPump? _pump;
        private Timer? _debounce;
        private Timer? _heartbeat;
        private string _lastJson = "";
        private bool _disposed;

        internal uint ExcludePid { get; set; } = (uint)Environment.ProcessId;

        public RunningAppsService(IHubContext<DeckHub>? hub, ILogger<RunningAppsService>? logger = null)
        {
            _hub = hub;
            _logger = logger;
        }

        private sealed record ProcInfo(string? Path, string Name, bool Elevated, string OwnName);

        private static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        public int WatcherCount
        {
            get { lock (_gate) return _watchers.Count; }
        }

        public bool IsPumping
        {
            get { lock (_gate) return _pump != null; }
        }

        public void Watch(string connectionId)
        {
            lock (_gate)
            {
                if (_disposed || !_watchers.Add(connectionId)) return;
                if (_watchers.Count != 1) return;
                _lastJson = "";
                _pump = new WinEventPump(MarkDirty, _logger);
                _pump.Start();
                _debounce = new Timer(_ => _ = PushAsync(), null, Timeout.Infinite, Timeout.Infinite);
                _heartbeat = new Timer(_ => MarkDirty(), null, Heartbeat, Heartbeat);
            }
        }

        public void Unwatch(string connectionId)
        {
            WinEventPump? pump = null;
            lock (_gate)
            {
                if (!_watchers.Remove(connectionId) || _watchers.Count != 0) return;
                pump = _pump;
                _pump = null;
                _debounce?.Dispose();
                _heartbeat?.Dispose();
                _debounce = _heartbeat = null;
            }
            pump?.Stop();
        }

        private void MarkDirty()
        {
            lock (_gate) _debounce?.Change(Debounce, Timeout.InfiniteTimeSpan);
        }

        private async Task PushAsync()
        {
            string[] targets;
            lock (_gate)
            {
                if (_watchers.Count == 0 || _disposed) return;
                targets = _watchers.ToArray();
            }
            try
            {
                var payload = Payload();
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                lock (_gate)
                {
                    if (json == _lastJson) return;
                    _lastJson = json;
                }
                if (_hub != null) await _hub.Clients.Clients(targets).SendAsync(Event, payload);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "RunningApps: falha ao enviar");
            }
        }

        public object Payload() => new { apps = List().Select(Dto).ToList() };

        public static object Dto(RunningApp a) => new
        {
            id = a.Id,
            name = a.Name,
            processName = a.ProcessName,
            title = a.Main?.Title ?? "",
            foreground = a.Foreground,
            minimized = a.Minimized,
            elevated = a.Elevated,
            windowCount = a.Windows.Count,
            windows = a.Windows.Select(w => new { id = w.Id, title = w.Title, minimized = w.Minimized, foreground = w.Foreground }),
        };

        public List<RunningApp> List()
        {
            return RunningAppsGrouper.Group(EnumerateWindows(), NameFor);
        }

        private string NameFor(string? path, string processName)
        {
            if (path == null) return processName;
            return _names.GetOrAdd(path.ToLowerInvariant(), _ =>
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    var name = (vi.FileDescription ?? "").Trim();
                    if (name.Length == 0) name = (vi.ProductName ?? "").Trim();
                    if (name.Length == 0) name = System.IO.Path.GetFileNameWithoutExtension(path);
                    return name.Replace("®", "").Replace("™", "").Trim();
                }
                catch
                {
                    return processName;
                }
            });
        }

        public byte[]? IconFor(string id)
        {
            if (_icons.TryGetValue(id, out var cached)) return cached;
            var app = List().FirstOrDefault(a => a.Id == id);
            if (app?.ExePath == null) return null;
            byte[]? png = null;
            try { png = AppLibrary.ExtractIconPng(app.ExePath); } catch { }
            if (_icons.Count > 200) _icons.Clear();
            _icons[id] = png;
            return png;
        }

        internal List<RawWindow> EnumerateWindows()
        {
            var result = new List<RawWindow>();
            var foreground = Native.GetForegroundWindow();
            Native.EnumWindows((hwnd, _) =>
            {
                try
                {
                    var w = Inspect(hwnd, foreground);
                    if (w != null) result.Add(w);
                }
                catch {  }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private RawWindow? Inspect(IntPtr hwnd, IntPtr foreground)
        {
            if (!Native.IsWindowVisible(hwnd)) return null;
            if (Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return null;

            var ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return null;
            if ((ex & Native.WS_EX_NOACTIVATE) != 0) return null;

            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return null;

            var length = Native.GetWindowTextLength(hwnd);
            if (length == 0) return null;
            var sb = new StringBuilder(length + 1);
            Native.GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            var iconic = Native.IsIconic(hwnd);
            if (!iconic)
            {
                if (!Native.GetWindowRect(hwnd, out var r) || r.Right - r.Left < 2 || r.Bottom - r.Top < 2) return null;
            }

            var cls = new StringBuilder(256);
            Native.GetClassName(hwnd, cls, cls.Capacity);
            var className = cls.ToString();

            Native.GetWindowThreadProcessId(hwnd, out var framePid);
            var pid = framePid;
            if (className == "ApplicationFrameWindow") pid = RealPackagedPid(hwnd, framePid);
            if (pid == 0 || pid == ExcludePid || framePid == ExcludePid) return null;

            var info = ProcInfoFor(pid);
            if (!RunningAppsGrouper.IsUserFacing(info.OwnName, className)) return null;

            return new RawWindow(hwnd.ToInt64(), framePid, pid, title, iconic, hwnd == foreground, info.Path, info.Name, info.Elevated);
        }

        private static uint RealPackagedPid(IntPtr frame, uint hostPid)
        {
            uint found = 0;
            Native.EnumChildWindows(frame, (child, _) =>
            {
                Native.GetWindowThreadProcessId(child, out var childPid);
                if (childPid != 0 && childPid != hostPid)
                {
                    found = childPid;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private ProcInfo ProcInfoFor(uint pid)
        {
            var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) return new ProcInfo(null, SafeProcessName(pid), false, SafeProcessName(pid));
            try
            {
                long started = Native.GetProcessTimes(handle, out var created, out _, out _, out _) ? created : 0;
                if (_procs.TryGetValue((pid, started), out var cached)) return cached;

                string? path = null;
                var buf = new StringBuilder(1024);
                var size = buf.Capacity;
                if (Native.QueryFullProcessImageName(handle, 0, buf, ref size)) path = buf.ToString();
                var name = path != null ? System.IO.Path.GetFileNameWithoutExtension(path) : SafeProcessName(pid);
                var ownName = name;
                var rootPath = path;
                if (path != null) (rootPath, name) = ResolveOwner(handle, path, started, name);
                var info = new ProcInfo(rootPath, name, IsElevated(handle), ownName);
                if (_procs.Count > 400) _procs.Clear();
                _procs[(pid, started)] = info;
                return info;
            }
            finally
            {
                Native.CloseHandle(handle);
            }
        }

        private static (string Path, string Name) ResolveOwner(IntPtr childHandle, string path, long childStarted, string name)
        {
            var currentPath = path;
            var currentName = name;
            var handle = childHandle;
            var started = childStarted;
            var opened = new List<IntPtr>();
            try
            {
                for (var depth = 0; depth < 3; depth++)
                {
                    var pbi = new Native.PROCESS_BASIC_INFORMATION();
                    if (Native.NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf<Native.PROCESS_BASIC_INFORMATION>(), out _) != 0) break;
                    var parentPid = (uint)pbi.InheritedFromUniqueProcessId.ToInt64();
                    if (parentPid == 0) break;
                    var parent = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, parentPid);
                    if (parent == IntPtr.Zero) break;
                    opened.Add(parent);
                    if (!Native.GetProcessTimes(parent, out var parentStarted, out _, out _, out _) || parentStarted == 0 || parentStarted > started) break;
                    var buf = new StringBuilder(1024);
                    var size = buf.Capacity;
                    if (!Native.QueryFullProcessImageName(parent, 0, buf, ref size)) break;
                    var parentPath = buf.ToString();
                    if (!RunningAppsGrouper.IsOwnerOf(parentPath, currentPath, WindowsDir)) break;
                    currentPath = parentPath;
                    currentName = System.IO.Path.GetFileNameWithoutExtension(parentPath);
                    handle = parent;
                    started = parentStarted;
                }
            }
            catch
            {
            }
            finally
            {
                foreach (var h in opened) Native.CloseHandle(h);
            }
            return (currentPath, currentName);
        }

        private static string SafeProcessName(uint pid)
        {
            try { return Process.GetProcessById((int)pid).ProcessName; } catch { return ""; }
        }

        private static bool IsElevated(IntPtr process)
        {
            if (!Native.OpenProcessToken(process, Native.TOKEN_QUERY, out var token)) return false;
            try
            {
                return Native.GetTokenInformation(token, Native.TokenElevation, out int elevation, sizeof(int), out _) && elevation != 0;
            }
            finally
            {
                Native.CloseHandle(token);
            }
        }

        internal (AppActionResult? Error, RunningApp? App, AppWindow? Window) Resolve(string id, string? windowId)
        {
            var app = List().FirstOrDefault(a => a.Id == id);
            if (app == null) return (new AppActionResult(AppActionOutcome.AppGone), null, null);
            AppWindow? window;
            if (string.IsNullOrEmpty(windowId)) window = app.Main;
            else
            {
                if (!RunningAppsGrouper.TryParseWindowId(windowId, out var hwnd, out var framePid))
                    return (new AppActionResult(AppActionOutcome.WindowGone), app, null);
                window = app.Windows.FirstOrDefault(w => w.Hwnd == hwnd && w.FramePid == framePid);
            }
            if (window == null || !Native.IsWindow(new IntPtr(window.Hwnd)))
                return (new AppActionResult(AppActionOutcome.WindowGone), app, null);
            if (app.Elevated && !HardwareSensors.IsElevated)
                return (new AppActionResult(AppActionOutcome.Elevated), app, window);
            return (null, app, window);
        }

        public async Task<AppActionResult> FocusAsync(string id, string? windowId = null)
        {
            var (error, _, window) = Resolve(id, windowId);
            if (error != null) return error;
            var hwnd = new IntPtr(window!.Hwnd);

            if (Native.IsIconic(hwnd))
            {
                Native.ShowWindowAsync(hwnd, Native.SW_RESTORE);
                await Task.Delay(180);
            }
            if (!Native.IsWindow(hwnd)) return new AppActionResult(AppActionOutcome.WindowGone);

            if (!Native.SetForegroundWindow(hwnd) && Native.GetForegroundWindow() != hwnd)
            {
                Native.SwitchToThisWindow(hwnd, true);
            }
            await Task.Delay(120);
            return Native.GetForegroundWindow() == hwnd
                ? new AppActionResult(AppActionOutcome.Ok)
                : new AppActionResult(AppActionOutcome.FocusDenied);
        }

        public AppActionResult Minimize(string id, string? windowId = null)
        {
            var (error, _, window) = Resolve(id, windowId);
            if (error != null) return error;
            if (!Native.IsIconic(new IntPtr(window!.Hwnd)))
                Native.ShowWindowAsync(new IntPtr(window.Hwnd), Native.SW_MINIMIZE);
            return new AppActionResult(AppActionOutcome.Ok);
        }

        public async Task<AppActionResult> CloseAsync(string id, string? windowId = null)
        {
            var (error, _, window) = Resolve(id, windowId);
            if (error != null) return error;
            var hwnd = new IntPtr(window!.Hwnd);
            if (!Native.PostMessage(hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            {
                return Marshal.GetLastWin32Error() == 5
                    ? new AppActionResult(AppActionOutcome.Elevated)
                    : new AppActionResult(AppActionOutcome.WindowGone);
            }
            for (var i = 0; i < 25; i++)
            {
                await Task.Delay(100);
                if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd)) return new AppActionResult(AppActionOutcome.Ok);
            }
            return new AppActionResult(AppActionOutcome.StillOpen);
        }

        public void Dispose()
        {
            WinEventPump? pump;
            lock (_gate)
            {
                _disposed = true;
                pump = _pump;
                _pump = null;
                _debounce?.Dispose();
                _heartbeat?.Dispose();
                _debounce = _heartbeat = null;
                _watchers.Clear();
            }
            pump?.Stop();
        }

        private sealed class WinEventPump
        {
            private readonly Action _onChange;
            private readonly ILogger? _logger;
            private readonly ManualResetEventSlim _ready = new();
            private Thread? _thread;
            private uint _threadId;
            private readonly List<IntPtr> _hooks = new();
            private Native.WinEventProc? _proc;

            public WinEventPump(Action onChange, ILogger? logger)
            {
                _onChange = onChange;
                _logger = logger;
            }

            public void Start()
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "KZ.RunningApps.WinEvents" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                _ready.Wait(2000);
            }

            public void Stop()
            {
                if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _thread?.Join(2000);
            }

            private void Run()
            {
                try
                {
                    _threadId = Native.GetCurrentThreadId();
                    Native.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
                    _proc = OnEvent;
                    foreach (var (min, max) in new (uint, uint)[]
                    {
                        (0x0003, 0x0003), (0x0016, 0x0017), (0x8001, 0x8003), (0x800C, 0x800C), (0x8017, 0x8018),
                    })
                    {
                        var h = Native.SetWinEventHook(min, max, IntPtr.Zero, _proc, 0, 0,
                            Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
                        if (h != IntPtr.Zero) _hooks.Add(h);
                    }
                    _ready.Set();
                    while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                    {
                        Native.TranslateMessage(ref msg);
                        Native.DispatchMessage(ref msg);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "RunningApps: laco de eventos terminou com erro");
                }
                finally
                {
                    foreach (var h in _hooks) Native.UnhookWinEvent(h);
                    _hooks.Clear();
                    _ready.Set();
                }
            }

            private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
            {
                if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0) return;
                if (evt != 0x0003 && Native.GetAncestor(hwnd, Native.GA_ROOT) != hwnd) return;
                _onChange();
            }
        }

        private static class Native
        {
            public const uint GW_OWNER = 4;
            public const int GWL_EXSTYLE = -20;
            public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
            public const int DWMWA_CLOAKED = 14;
            public const int SW_MINIMIZE = 6, SW_RESTORE = 9;
            public const uint WM_CLOSE = 0x0010, WM_QUIT = 0x0012;
            public const uint GA_ROOT = 2;
            public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            public const uint TOKEN_QUERY = 0x0008;
            public const int TokenElevation = 20;
            public const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;

            public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
            public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT { public int Left, Top, Right, Bottom; }

            [StructLayout(LayoutKind.Sequential)]
            public struct PROCESS_BASIC_INFORMATION
            {
                public IntPtr Reserved1;
                public IntPtr PebBaseAddress;
                public IntPtr Reserved2_0;
                public IntPtr Reserved2_1;
                public IntPtr UniqueProcessId;
                public IntPtr InheritedFromUniqueProcessId;
            }

            [DllImport("ntdll.dll")]
            public static extern int NtQueryInformationProcess(IntPtr process, int cls, ref PROCESS_BASIC_INFORMATION info, int length, out int returned);

            [StructLayout(LayoutKind.Sequential)]
            public struct MSG
            {
                public IntPtr hwnd;
                public uint message;
                public IntPtr wParam, lParam;
                public uint time;
                public int x, y;
            }

            [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
            [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);
            [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
            [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
            [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
            [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hwnd);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
            [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
            [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
            [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab);
            [DllImport("user32.dll", SetLastError = true)] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
            [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
            [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
            [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
            [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
            [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
            [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
            [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG msg);
            [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
            [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
            [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
            [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
            [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int length, out int returned);

            [DllImport("kernel32.dll", EntryPoint = "GetProcessTimes")]
            private static extern bool GetProcessTimesRaw(IntPtr process, out long create, out long exit, out long kernel, out long user);

            public static bool GetProcessTimes(IntPtr process, out long create, out long exit, out long kernel, out long user) =>
                GetProcessTimesRaw(process, out create, out exit, out kernel, out user);
        }
    }
}
