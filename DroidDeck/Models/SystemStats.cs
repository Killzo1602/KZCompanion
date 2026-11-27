using System.Collections.Generic;

namespace DroidDeck.Models
{
    public class SystemStats
    {
        public float CpuUsage { get; set; }
        public float RamUsage { get; set; }
        public float RamTotal { get; set; }
        public float RamAvailable { get; set; }
        public float NetUpKBps { get; set; }
        public float NetDownKBps { get; set; }
        public float GpuUsage { get; set; }

        public float? CpuTemp { get; set; }
        public float? CpuPower { get; set; }
        public float? GpuTemp { get; set; }
        public float? GpuHotspot { get; set; }
        public float? GpuPower { get; set; }
        public float? SsdTemp { get; set; }
        public float? CpuClock { get; set; }
        public float? GpuClock { get; set; }
        public float? GpuMemUsed { get; set; }
        public float? GpuMemTotal { get; set; }
        public int CpuSensorCount { get; set; }
        public bool SensorsElevated { get; set; }
        public string SensorSource { get; set; } = "local";
        public bool CpuDriverMissing { get; set; }
        public string NetAdapter { get; set; } = "";
        public List<DriveTemp> Drives { get; set; } = new();
        public List<FanStat> Fans { get; set; } = new();

        public List<DiskStat> Disks { get; set; } = new();
    }

    public class DiskStat
    {
        public string Name { get; set; } = "";

        public string Label { get; set; } = "";

        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
    }
}

namespace DroidDeck.Models
{
    public class FanStat
    {
        public string Name { get; set; } = "";
        public int Rpm { get; set; }
    }

    public class DriveTemp
    {
        public string Name { get; set; } = "";
        public float Temp { get; set; }
    }
}
