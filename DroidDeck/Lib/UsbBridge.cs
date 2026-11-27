using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Lib
{
    public class UsbBridge : BackgroundService
    {
        public const string PlatformToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(4);

        private readonly ILogger<UsbBridge> _logger;
        private readonly object _lock = new();
        private HashSet<string> _forwarded = new();

        public static UsbBridge? Instance { get; private set; }

        public UsbBridge(ILogger<UsbBridge> logger)
        {
            _logger = logger;
            Instance = this;
        }

        public event Action? Changed;

        public IReadOnlyCollection<string> Devices
        {
            get { lock (_lock) return _forwarded.ToList(); }
        }

        private static string SettingsPath => Path.Combine(AppPaths.LocalDir, "usb.json");

        private class Settings
        {
            public bool Enabled { get; set; }
        }

        public static bool Enabled
        {
            get
            {
                try
                {
                    if (File.Exists(SettingsPath))
                        return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))?.Enabled ?? false;
                }
                catch { }
                return FindAdb() != null;
            }
            set
            {
                try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings { Enabled = value })); }
                catch { }
            }
        }

        private static string OwnPlatformTools => Path.Combine(AppPaths.LocalDir, "platform-tools");

        public static string? FindAdb()
        {
            var candidates = new List<string>();
            var sdk = Environment.GetEnvironmentVariable("ANDROID_HOME") ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            if (!string.IsNullOrEmpty(sdk)) candidates.Add(Path.Combine(sdk, "platform-tools", "adb.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android", "Sdk", "platform-tools", "adb.exe"));
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (dir.Length > 0) candidates.Add(Path.Combine(dir.Trim('"'), "adb.exe"));
            }
            candidates.Add(Path.Combine(OwnPlatformTools, "adb.exe"));
            return candidates.FirstOrDefault(File.Exists);
        }

        public static async Task DownloadAdbAsync(CancellationToken ct = default)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var zip = Path.Combine(Path.GetTempPath(), "droiddeck-platform-tools.zip");
            await using (var file = File.Create(zip))
            await using (var stream = await http.GetStreamAsync(PlatformToolsUrl, ct))
                await stream.CopyToAsync(file, ct);
            try
            {
                if (Directory.Exists(OwnPlatformTools)) Directory.Delete(OwnPlatformTools, true);
                ZipFile.ExtractToDirectory(zip, AppPaths.LocalDir, overwriteFiles: true);
            }
            finally
            {
                try { File.Delete(zip); } catch { }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (Enabled && FindAdb() is { } adb) await SyncAsync(adb, stoppingToken);
                    else SetForwarded(new HashSet<string>());
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogDebug("USB: {Message}", ex.Message);
                }
                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task SyncAsync(string adb, CancellationToken ct)
        {
            var devices = ParseDevices(await RunAsync(adb, "devices", ct));
            var now = await ForwardAllAsync(devices, async serial =>
            {
                var list = await RunAsync(adb, $"-s {serial} reverse --list", ct);
                if (HasReverse(list, PairingInfo.Port)) return;
                await RunAsync(adb, $"-s {serial} reverse tcp:{PairingInfo.Port} tcp:{PairingInfo.Port}", ct);
                _logger.LogInformation("USB: celular {Serial} conectado pelo cabo", serial);
            }, (serial, ex) => _logger.LogDebug("USB {Serial}: {Message}", serial, ex.Message), ct);
            SetForwarded(now);
        }

        public static async Task<HashSet<string>> ForwardAllAsync(IEnumerable<string> serials,
            Func<string, Task> forward, Action<string, Exception> failed, CancellationToken ct)
        {
            var done = new HashSet<string>();
            foreach (var serial in serials)
            {
                try
                {
                    await forward(serial);
                    done.Add(serial);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    failed(serial, ex);
                }
            }
            return done;
        }

        private void SetForwarded(HashSet<string> now)
        {
            bool changed;
            lock (_lock)
            {
                changed = !now.SetEquals(_forwarded);
                _forwarded = now;
            }
            if (changed) Changed?.Invoke();
        }

        public static async Task<string> RunAsync(string exe, string args, CancellationToken ct, TimeSpan? limit = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("adb nao iniciou");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(limit ?? TimeSpan.FromSeconds(15));
            try
            {
                var output = p.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = p.StandardError.ReadToEndAsync(timeout.Token);
                await p.WaitForExitAsync(timeout.Token);
                if (p.ExitCode != 0)
                    throw new InvalidOperationException($"adb {args}: {(await error).Trim()}");
                return await output;
            }
            catch (OperationCanceledException)
            {
                try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { }
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException($"adb {args}: sem resposta");
            }
        }

        public static List<string> ParseDevices(string output) =>
            output.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("List of devices") && !l.StartsWith("*"))
                .Select(l => l.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length >= 2 && p[1] == "device")
                .Select(p => p[0])
                .ToList();

        public static bool HasReverse(string output, int port) =>
            output.Split('\n').Any(l => l.Contains($"tcp:{port} tcp:{port}"));

        public static bool IsForwarded(IPAddress? remoteIp, int remotePort)
        {
            if (remoteIp == null || !OperatingSystem.IsWindows()) return false;
            if (remoteIp.IsIPv4MappedToIPv6) remoteIp = remoteIp.MapToIPv4();
            if (!IPAddress.IsLoopback(remoteIp)) return false;
            var pid = OwnerOfLoopbackClient(remotePort, PairingInfo.Port);
            if (pid is not int id) return false;
            try
            {
                using var proc = Process.GetProcessById(id);
                return proc.ProcessName.Equals("adb", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_CONNECTIONS = 4;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct TcpRowOwnerPid
        {
            public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort, OwningPid;
        }

        private static int NetPort(uint raw) => (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));

        public static int? OwnerOfLoopbackClient(int clientPort, int serverPort)
        {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_CONNECTIONS, 0) != 0)
                    return null;
                var count = Marshal.ReadInt32(buffer);
                var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<TcpRowOwnerPid>(buffer + 4 + i * rowSize);
                    if (NetPort(row.LocalPort) == clientPort && NetPort(row.RemotePort) == serverPort)
                        return (int)row.OwningPid;
                }
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
