using System;
using System.Collections.Generic;
using System.Linq;
using DroidDeck.Lib;
using DroidDeck.Models;
using LibreHardwareMonitor.Hardware;

namespace DroidDeck.Services
{
    /// <summary>
    /// Temperaturas, ventoinhas e potencia pelo LibreHardwareMonitorLib (embutido: nao precisa
    /// do programa separado). Sensor que o hardware/driver nao expoe fica null e o app mostra
    /// "Indisponivel" -- nunca inventamos valor. Varios sensores (CPU, placa-mae) so aparecem
    /// com o KZ Companion aberto como administrador.
    /// </summary>
    public sealed class HardwareSensors : IDisposable
    {
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

        /// <summary>Le os sensores e preenche os campos de <paramref name="stats"/>.</summary>
        public void Fill(SystemStats stats)
        {
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
                        if (hit.Sensor != null) return Valid(hit.Sensor.Value);
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

                var gpuFilter = all.Any(x => IsGpu(x.Hw)) ? (Func<IHardware, bool>)IsGpu : IsAnyGpu;
                stats.GpuTemp = Pick(gpuFilter, SensorType.Temperature, "GPU Core", "GPU Temperature", "Core");
                stats.GpuHotspot = Pick(gpuFilter, SensorType.Temperature, "Hot Spot", "Hotspot", "Junction");
                stats.GpuPower = Pick(gpuFilter, SensorType.Power, "GPU Package", "GPU Power", "Package");

                stats.Drives = all
                    .Where(x => x.Hw.HardwareType == HardwareType.Storage && x.Sensor.SensorType == SensorType.Temperature && x.Sensor.Value.HasValue)
                    .GroupBy(x => x.Hw.Name)
                    .Select(g => new DriveTemp { Name = g.Key, Temp = g.Max(x => x.Sensor.Value!.Value) })
                    .ToList();
                stats.SsdTemp = stats.Drives.Count > 0 ? stats.Drives.Max(d => d.Temp) : null;

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

        private static float? Valid(float? v) => v is { } f && !float.IsNaN(f) && f > 0 && f < 5000 ? f : null;

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
