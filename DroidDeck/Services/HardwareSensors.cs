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
        /// <summary>Roda como administrador? Sem isso o LibreHardwareMonitor nao carrega o driver e a CPU nao expoe temperatura/potencia.</summary>
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

        /// <summary>
        /// O LibreHardwareMonitorLib 0.9.x le MSR/SMN (temperatura e potencia das CPUs AMD Zen e
        /// Intel) pelo driver PawnIO, assinado e separado. Sem ele esses sensores ficam em 0 mesmo
        /// como administrador; SSD e GPU nao dependem dele.
        /// </summary>
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

        /// <summary>Despeja todos os sensores vistos (diagnostico: --sensors-dump).</summary>
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

        /// <summary>Le os sensores e preenche os campos de <paramref name="stats"/>.</summary>
        public void Fill(SystemStats stats)
        {
            // App sem elevacao + auxiliar elevado conectado: as leituras completas (CPU, SSD, ventoinhas)
            // vem dele; sem auxiliar, le localmente (GPU funciona sem administrador).
            if (!IsElevated && SensorHelper.TryGet(out var h))
            {
                stats.CpuTemp = h.CpuTemp; stats.CpuPower = h.CpuPower; stats.GpuTemp = h.GpuTemp;
                stats.GpuHotspot = h.GpuHotspot; stats.GpuPower = h.GpuPower; stats.SsdTemp = h.SsdTemp;
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

                // A GPU "de verdade": a que tem temperatura de nucleo (a integrada do Ryzen so expoe
                // SoC) e, entre as que tem, a NVIDIA/AMD com ventoinha. Evita misturar iGPU e dGPU.
                var gpuHw = all.Where(x => IsAnyGpu(x.Hw)).Select(x => x.Hw).Distinct()
                    .OrderByDescending(h => all.Any(x => x.Hw == h && x.Sensor.SensorType == SensorType.Temperature && x.Sensor.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) && Valid(x.Sensor.Value) != null))
                    .ThenByDescending(h => all.Any(x => x.Hw == h && x.Sensor.SensorType == SensorType.Fan))
                    .ThenByDescending(h => h.HardwareType == HardwareType.GpuNvidia)
                    .FirstOrDefault();
                Func<IHardware, bool> gpuFilter = h => gpuHw != null && h == gpuHw;
                stats.GpuTemp = Pick(gpuFilter, SensorType.Temperature, "GPU Core", "GPU Temperature", "Core");
                stats.GpuHotspot = Pick(gpuFilter, SensorType.Temperature, "Hot Spot", "Hotspot", "Junction");
                stats.GpuPower = Pick(gpuFilter, SensorType.Power, "GPU Package", "GPU Power", "Package");

                // Um valor por unidade (o "Temperature" principal ou o composto do NVMe): ignora
                // limites ("Warning/Critical") e sensores secundarios, que duplicavam e
                // inflavam a leitura. Nao tentamos casar unidade com letra de particao.
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
