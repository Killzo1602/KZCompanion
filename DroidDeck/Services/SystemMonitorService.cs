using DroidDeck.Hubs;
using DroidDeck.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DroidDeck.Services
{
    public class SystemMonitorService : BackgroundService
    {
        private readonly ILogger<SystemMonitorService> _logger;
        private readonly IHubContext<DeckHub> _hubContext;
        private readonly MediaControlService _media;
        private readonly HardwareSensors _sensors;
        private volatile SystemStats? _lastSensors;
        private PerformanceCounter? _cpuCounter;

        // Estado para throughput de rede (deltas por segundo).
        private DateTime _lastNetTime = DateTime.UtcNow;

        // Counters de GPU (uso) por instância de engine 3D; cacheados entre ticks.
        private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new();
        // A enumeração de instâncias ("GetInstanceNames") é cara e aloca uma string por
        // engine de GPU a cada chamada. Só rescaneia a cada N ticks; entre eles, só lê os
        // counters já cacheados (evita pressão de GC constante no loop de 1s).
        private int _gpuTick = 0;

        // Espaço em disco muda devagar: lê a cada 30 s e repete o último nos outros ticks.
        private const int DiskEveryTicks = 30;
        private List<DiskStat>? _disks;
        private const int GpuRescanEveryTicks = 30;

        public SystemMonitorService(ILogger<SystemMonitorService> logger, IHubContext<DeckHub> hubContext, MediaControlService media, HardwareSensors sensors)
        {
            _logger = logger;
            _hubContext = hubContext;
            _media = media;
            _sensors = sensors;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!OperatingSystem.IsWindows())
            {
                _logger.LogWarning("SystemMonitorService is only supported on Windows.");
                return;
            }

            try
            {
                try
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                }
                catch
                {
                    // Fallback for PT-BR
                    try { _cpuCounter = new PerformanceCounter("Processador", "% tempo de processador", "_Total"); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to init CPU Counter"); }
                }

                if (_cpuCounter != null) _cpuCounter.NextValue();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal Error initializing Monitors");
                return;
            }

            _ = Task.Factory.StartNew(() => SensorLoop(stoppingToken), stoppingToken,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);

            int tick = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Sem clientes ouvindo, não computa nem transmite nada — evita o poll
                    // de mídia WinRT (caro) e as alocações de rede/GPU 24/7 com o app ocioso.
                    if (DeckHub.ConnectedCount <= 0)
                    {
                        try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
                        continue;
                    }

                    var tickStart = Stopwatch.GetTimestamp();
                    var cpu = _cpuCounter?.NextValue() ?? 0;

                    // RAM via GlobalMemoryStatusEx: total, disponível e % de uso de uma vez.
                    var mem = new MEMORYSTATUSEX();
                    float ramTotal = 0, ramAvailable = 0, ramLoad = 0;
                    if (GlobalMemoryStatusEx(mem))
                    {
                        ramTotal = mem.ullTotalPhys / (1024f * 1024f); // MB
                        ramAvailable = mem.ullAvailPhys / (1024f * 1024f); // MB
                        ramLoad = mem.dwMemoryLoad; // % usada (0-100)
                    }

                    var (netUp, netDown) = SampleNetwork();

                    var stats = new SystemStats
                    {
                        CpuUsage = (float)Math.Round(cpu, 1),
                        RamTotal = ramTotal,
                        RamAvailable = ramAvailable,
                        RamUsage = ramLoad,
                        NetUpKBps = netUp,
                        NetDownKBps = netDown,
                        GpuUsage = SampleGpu(),
                        Disks = tick % DiskEveryTicks == 0 || _disks == null ? (_disks = SampleDisks()) : _disks,
                    };

                    // Sensores vêm do cache da thread própria (SensorLoop): nunca bloqueiam o envio.
                    var ls = _lastSensors;
                    if (ls != null)
                    {
                        stats.CpuTemp = ls.CpuTemp; stats.CpuPower = ls.CpuPower; stats.GpuTemp = ls.GpuTemp;
                        stats.GpuHotspot = ls.GpuHotspot; stats.GpuPower = ls.GpuPower; stats.SsdTemp = ls.SsdTemp;
                        stats.Drives = ls.Drives; stats.Fans = ls.Fans;
                        stats.CpuSensorCount = ls.CpuSensorCount;
                        stats.CpuDriverMissing = ls.CpuDriverMissing;
                        stats.SensorSource = ls.SensorSource;
                    }
                    stats.SensorsElevated = HardwareSensors.IsElevated;
                    stats.NetAdapter = _netAdapter;

                    // Broadcast to clients listening on "ReceiveSystemStats"
                    await _hubContext.Clients.All.SendAsync("ReceiveSystemStats", stats, cancellationToken: stoppingToken);
                    var tickMs = Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds;
                    if (tickMs > 1500) _logger.LogWarning("Ciclo de estatisticas lento: {ms} ms (CPU/GPU sob carga?)", (int)tickMs);

                    // Estado de reprodução de mídia (pra botões play/pause refletirem).
                    // A cada 3s: o acesso WinRT (RequestAsync) é caro; 1s era exagero.
                    if (tick % 3 == 0)
                    {
                        try
                        {
                            var playing = await _media.IsAnythingPlayingAsync();
                            await _hubContext.Clients.All.SendAsync("ReceiveMediaStatus",
                                new { playing }, cancellationToken: stoppingToken);
                        }
                        catch { }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error broadcasting system stats");
                }

                tick++;
                try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
            }

            // Libera os PerformanceCounter ao encerrar o serviço.
            try { _cpuCounter?.Dispose(); } catch { }
            foreach (var c in _gpuCounters.Values) { try { c.Dispose(); } catch { } }
            _gpuCounters.Clear();
        }

        /// <summary>Unidades fixas prontas (HD/SSD; pendrive e rede ficam de fora), em ordem de letra.</summary>
        public static List<DiskStat> SampleDisks()
        {
            var list = new List<DiskStat>();
            try
            {
                foreach (var d in System.IO.DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != System.IO.DriveType.Fixed || !d.IsReady || d.TotalSize <= 0) continue;
                        list.Add(new DiskStat
                        {
                            Name = d.Name.TrimEnd('\\'),
                            Label = d.VolumeLabel ?? "",
                            TotalBytes = d.TotalSize,
                            FreeBytes = d.TotalFreeSpace,
                        });
                    }
                    catch { } // unidade que some no meio da leitura
                }
            }
            catch { }
            return list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Vazao real por adaptador fisico: delta de bytes (IPv4+IPv6) / tempo, so de placas
        // ativas. Virtuais (Hyper-V, WSL, VPN, Bluetooth) ficam de fora: contariam o mesmo
        // trafego duas vezes ou trafego interno. Contador que recua (adaptador reiniciou)
        // so descarta aquela amostra.
        private readonly Dictionary<string, (long Sent, long Recv)> _netLast = new();
        private string _netAdapter = "";

        private static readonly string[] VirtualMarkers =
        {
            "virtual", "vethernet", "hyper-v", "vmware", "virtualbox", "wsl", "loopback", "bluetooth",
            "tap-", "tap ", "wintun", "wireguard", "openvpn", "npcap", "pseudo", "teredo", "isatap",
            "vpn", "zerotier", "tailscale", "miniport",
        };

        private static bool IsPhysical(NetworkInterface ni)
        {
            if (ni.OperationalStatus != OperationalStatus.Up) return false;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) return false;
            var text = (ni.Name + " " + ni.Description).ToLowerInvariant();
            return !VirtualMarkers.Any(text.Contains);
        }

        private (float up, float down) SampleNetwork()
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastNetTime).TotalSeconds;
            _lastNetTime = now;
            double up = 0, down = 0, bestRate = -1;
            var seen = new HashSet<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!IsPhysical(ni)) continue;
                    var st = ni.GetIPStatistics();
                    long sent = st.BytesSent, recv = st.BytesReceived;
                    seen.Add(ni.Id);
                    if (_netLast.TryGetValue(ni.Id, out var prev) && elapsed > 0.2)
                    {
                        long ds = sent - prev.Sent, dr = recv - prev.Recv;
                        if (ds >= 0 && dr >= 0)
                        {
                            double u = ds / elapsed / 1024.0, d = dr / elapsed / 1024.0;
                            up += u; down += d;
                            if (u + d > bestRate) { bestRate = u + d; _netAdapter = ni.Name; }
                        }
                    }
                    _netLast[ni.Id] = (sent, recv);
                }
            }
            catch { }
            foreach (var key in _netLast.Keys.Where(k => !seen.Contains(k)).ToList()) _netLast.Remove(key);
            return ((float)up, (float)down);
        }

        /// <summary>Le os sensores numa thread propria, a cada 2 s, so com cliente conectado.</summary>
        private void SensorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (DeckHub.ConnectedCount > 0)
                    {
                        var t0 = Stopwatch.GetTimestamp();
                        var s = new SystemStats();
                        _sensors.Fill(s);
                        _lastSensors = s;
                        var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                        if (ms > 1000) _logger.LogWarning("Leitura de sensores lenta: {ms} ms", (int)ms);
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Leitura de sensores falhou"); }
                if (token.WaitHandle.WaitOne(2000)) break;
            }
        }

        private float SampleGpu()
        {
            try
            {
                // Rescan das instâncias só de tempos em tempos (caro). No 1º tick e a cada N.
                if (_gpuTick++ % GpuRescanEveryTicks == 0)
                {
                    var cat = new PerformanceCounterCategory("GPU Engine");
                    var names = cat.GetInstanceNames()
                        .Where(n => n.EndsWith("engtype_3D"))
                        .ToHashSet();

                    foreach (var n in names)
                    {
                        if (!_gpuCounters.ContainsKey(n))
                        {
                            var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true);
                            try { c.NextValue(); } catch { } // prime: 1ª leitura é 0
                            _gpuCounters[n] = c;
                        }
                    }
                    foreach (var key in _gpuCounters.Keys.Where(k => !names.Contains(k)).ToList())
                    {
                        try { _gpuCounters[key].Dispose(); } catch { }
                        _gpuCounters.Remove(key);
                    }
                }

                float sum = 0;
                foreach (var c in _gpuCounters.Values)
                {
                    try { sum += c.NextValue(); } catch { }
                }
                return Math.Min(sum, 100f);
            }
            catch
            {
                return 0; // GPU Engine indisponível neste sistema
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
    }
}
