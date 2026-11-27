using System;
using System.Collections.Generic;
using System.Linq;
using DroidDeck.Lib;
using DroidDeck.Models;
using LibreHardwareMonitor.Hardware;

namespace DroidDeck.Services
{
    public sealed class HardwareSensors : IDisposable
    {
        public static bool IsElevated
        {
            get
            {
                try
                {
                    using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                    return new System.Security.Principal.WindowsPrincipal(id)
                        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        public static bool PawnIoInstalled
        {
            get
            {
                try
                {
                    using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                    return k != null;
                }
                catch { return false; }
            }
        }

        public string Dump()
        {
            EnsureOpen();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Elevated: {IsElevated}");
            sb.AppendLine($"PawnIO driver installed: {PawnIoInstalled}");
            if (_computer == null) { sb.AppendLine("LibreHardwareMonitor nao abriu."); return sb.ToString(); }
            void Walk(IHardware hw, int depth)
            {
                hw.Update();
                sb.AppendLine($"{new string(' ', depth * 2)}[{hw.HardwareType}] {hw.Name}");
                foreach (var sn in hw.Sensors)
                    sb.AppendLine($"{new string(' ', depth * 2 + 2)}{sn.SensorType,-12} {sn.Name,-28} = {(sn.Value.HasValue ? sn.Value.Value.ToString("0.##") : "null")}");
                foreach (var sub in hw.SubHardware) Walk(sub, depth + 1);
            }
            foreach (var hw in _computer.Hardware) Walk(hw, 0);
            return sb.ToString();
        }

        private Computer? _computer;
        private bool _failed;

        private void EnsureOpen()
        {
            if (_computer != null || _failed) return;
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsStorageEnabled = true,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true,
                };
                _computer.Open();
            }
            catch (Exception ex)
            {
                _failed = true;
                _computer = null;
                Log.Warning($"Sensores de hardware indisponíveis: {ex.Message}");
            }
        }

        public void Fill(SystemStats stats)
        {
            if (!IsElevated && SensorHelper.TryGet(out var h))
            {
                stats.CpuTemp = h.CpuTemp; stats.CpuPower = h.CpuPower; stats.GpuTemp = h.GpuTemp;
                stats.GpuHotspot = h.GpuHotspot; stats.GpuPower = h.GpuPower; stats.SsdTemp = h.SsdTemp;
                stats.CpuClock = h.CpuClock; stats.GpuClock = h.GpuClock;
                stats.GpuMemUsed = h.GpuMemUsed; stats.GpuMemTotal = h.GpuMemTotal;
                stats.Drives = h.Drives; stats.Fans = h.Fans; stats.CpuSensorCount = h.CpuSensorCount;
                stats.SensorsElevated = h.SensorsElevated; stats.CpuDriverMissing = h.CpuDriverMissing;
                stats.SensorSource = "helper";
                return;
            }
            EnsureOpen();
            if (_computer == null) return;
            try
            {
                var all = new List<(IHardware Hw, ISensor Sensor)>();
                foreach (var hw in _computer.Hardware) Collect(hw, all);

                float? Pick(Func<IHardware, bool> hwFilter, SensorType type, params string[] names)
                {
                    var set = all.Where(x => hwFilter(x.Hw) && x.Sensor.SensorType == type && x.Sensor.Value.HasValue).ToList();
                    foreach (var n in names)
                    {
                        var hit = set.FirstOrDefault(x => x.Sensor.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
                        if (hit.Sensor != null) return Valid(hit.Sensor.Value, type);
                    }
                    return null;
                }

                bool IsCpu(IHardware h) => h.HardwareType == HardwareType.Cpu;
                bool IsGpu(IHardware h) => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd;
                bool IsAnyGpu(IHardware h) => IsGpu(h) || h.HardwareType == HardwareType.GpuIntel;

                stats.CpuTemp = Pick(IsCpu, SensorType.Temperature, "Package", "Tctl", "Tdie", "Average", "Core Max")
                    ?? Valid(all.Where(x => IsCpu(x.Hw) && x.Sensor.SensorType == SensorType.Temperature)
                                .Select(x => x.Sensor.Value).Where(v => v.HasValue).DefaultIfEmpty(null).Max());
                stats.CpuPower = Pick(IsCpu, SensorType.Power, "Package");

                var gpuHw = all.Where(x => IsAnyGpu(x.Hw)).Select(x => x.Hw).Distinct()
                    .OrderByDescending(h => all.Any(x => x.Hw == h && x.Sensor.SensorType == SensorType.Temperature && x.Sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) && Valid(x.Sensor.Value) != null))
                    .ThenByDescending(h => all.Any(x => x.Hw == h && x.Sensor.SensorType == SensorType.Fan))
                    .ThenByDescending(h => h.HardwareType == HardwareType.GpuNvidia)
                    .FirstOrDefault();
                Func<IHardware, bool> gpuFilter = h => gpuHw != null && h == gpuHw;
                stats.GpuTemp = Pick(gpuFilter, SensorType.Temperature, "GPU Core", "GPU Temperature", "Core");
                stats.GpuHotspot = Pick(gpuFilter, SensorType.Temperature, "Hot Spot", "Hotspot", "Junction");
                stats.GpuPower = Pick(gpuFilter, SensorType.Power, "GPU Package", "GPU Power", "Package");

                stats.CpuClock = Valid(all.Where(x => IsCpu(x.Hw) && x.Sensor.SensorType == SensorType.Clock
                        && x.Sensor.Name.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Sensor.Value).Where(v => v.HasValue).DefaultIfEmpty(null).Max());
                stats.GpuClock = Pick(gpuFilter, SensorType.Clock, "GPU Core");
                stats.GpuMemUsed = Pick(gpuFilter, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
                stats.GpuMemTotal = Pick(gpuFilter, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");

                stats.Drives = all
                    .Where(x => x.Hw.HardwareType == HardwareType.Storage && x.Sensor.SensorType == SensorType.Temperature && Valid(x.Sensor.Value) != null)
                    .GroupBy(x => x.Hw.Identifier.ToString())
                    .Select(g =>
                    {
                        var main = g.Where(x => !x.Sensor.Name.Contains("Warning", StringComparison.OrdinalIgnoreCase)
                                             && !x.Sensor.Name.Contains("Critical", StringComparison.OrdinalIgnoreCase)).Select(x => x.Sensor).ToList();
                        var pick = main.FirstOrDefault(x => x.Name.Equals("Temperature", StringComparison.OrdinalIgnoreCase))
                                   ?? main.FirstOrDefault(x => x.Name.Contains("Composite", StringComparison.OrdinalIgnoreCase))
                                   ?? main.FirstOrDefault();
                        return pick == null ? null : new DriveTemp { Name = g.First().Hw.Name, Temp = pick.Value!.Value };
                    })
                    .Where(d => d != null).Select(d => d!)
                    .ToList();
                stats.SsdTemp = stats.Drives.Count > 0 ? stats.Drives.Max(d => d.Temp) : null;
                stats.SensorsElevated = IsElevated;
                stats.CpuDriverMissing = !PawnIoInstalled;
                stats.CpuSensorCount = all.Count(x => IsCpu(x.Hw) && x.Sensor.SensorType == SensorType.Temperature && Valid(x.Sensor.Value) != null);

                stats.Fans = all
                    .Where(x => x.Sensor.SensorType == SensorType.Fan && x.Sensor.Value is > 0)
                    .Select(x => new FanStat { Name = x.Sensor.Name, Rpm = (int)x.Sensor.Value!.Value })
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warning($"Leitura de sensores falhou: {ex.Message}");
            }
        }

        private static float? Valid(float? v) => Valid(v, SensorType.Temperature);

        public static float? Valid(float? v, SensorType type)
        {
            var max = type switch { SensorType.SmallData => 4_000_000f, SensorType.Clock => 20_000f, _ => 5000f };
            return v is { } f && !float.IsNaN(f) && f > 0 && f < max ? f : null;
        }

        private static void Collect(IHardware hw, List<(IHardware, ISensor)> into)
        {
            hw.Update();
            foreach (var s in hw.Sensors) into.Add((hw, s));
            foreach (var sub in hw.SubHardware) Collect(sub, into);
        }

        public void Dispose()
        {
            try { _computer?.Close(); } catch { }
            _computer = null;
        }
    }
}
