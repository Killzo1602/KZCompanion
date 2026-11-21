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

        // Sensores de hardware: null = o PC/driver não expõe (o app mostra "Indisponível").
        public float? CpuTemp { get; set; }
        public float? CpuPower { get; set; }
        public float? GpuTemp { get; set; }
        public float? GpuHotspot { get; set; }
        public float? GpuPower { get; set; }
        public float? SsdTemp { get; set; }
        public List<DriveTemp> Drives { get; set; } = new();
        public List<FanStat> Fans { get; set; } = new();

        /// <summary>Unidades de disco fixas do PC (atualiza a cada 30 s).</summary>
        public List<DiskStat> Disks { get; set; } = new();
    }

    public class DiskStat
    {
        /// <summary>Letra da unidade: "C:".</summary>
        public string Name { get; set; } = "";

        /// <summary>Nome do volume no Windows ("Dados"); vazio quando não tem.</summary>
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
