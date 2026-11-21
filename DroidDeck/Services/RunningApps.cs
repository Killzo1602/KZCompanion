using System;
using System.Collections.Generic;
using System.Linq;

namespace DroidDeck.Services
{
    /// <summary>
    /// Uma janela de topo que o usuario enxerga (ja filtrada: visivel, com titulo, sem dona,
    /// fora de cloak). <see cref="Pid"/> e o processo REAL do aplicativo: nos apps da Loja a
    /// janela pertence ao ApplicationFrameHost (<see cref="FramePid"/>) e o Pid e o do app.
    /// </summary>
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

    /// <summary>Um aplicativo em execucao: todas as janelas e processos do mesmo executavel.</summary>
    public sealed class RunningApp
    {
        /// <summary>
        /// Identidade estavel: o mesmo hash do caminho do .exe que o mixer de audio usa como
        /// groupKey (ver AudioMixerService.ShortHash). E assim que o app casa com a sessao de audio.
        /// </summary>
        public string Id { get; init; } = "";
        public string Name { get; set; } = "";
        public string ProcessName { get; init; } = "";
        public string? ExePath { get; init; }
        public IReadOnlyList<uint> Pids { get; init; } = Array.Empty<uint>();
        public IReadOnlyList<AppWindow> Windows { get; init; } = Array.Empty<AppWindow>();
        public bool Foreground => Windows.Any(w => w.Foreground);
        public bool Minimized => Windows.Count > 0 && Windows.All(w => w.Minimized);
        public bool Elevated { get; init; }

        /// <summary>Janela principal: a em foco, senao a primeira fora da barra de tarefas (ordem Z).</summary>
        public AppWindow? Main => Windows.FirstOrDefault();
    }

    /// <summary>Agrupamento puro (sem Win32): e o que os testes exercitam.</summary>
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

        /// <summary>
        /// Um app por executavel (varios processos e varias janelas viram uma linha so). A ordem
        /// de entrada e a ordem Z (de cima para baixo): a janela em foco vem primeiro, depois as
        /// visiveis, depois as minimizadas.
        /// </summary>
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
                // O mesmo HWND nunca entra duas vezes.
                if (list.Any(x => x.Hwnd == w.Hwnd)) continue;
                list.Add(w);
            }

            var apps = new List<RunningApp>();
            foreach (var key in order)
            {
                var list = groups[key]
                    .OrderByDescending(w => w.Foreground)
                    .ThenBy(w => w.Minimized)
                    .ToList(); // OrderBy e estavel: a ordem Z se mantem dentro de cada faixa
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
            // Ordem estavel por nome: a lista nao pula quando o foco muda.
            return apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Processos de sistema e de shell que tem janela mas nao sao "aplicativos do usuario".
        /// Comparado sem maiusculas e sem .exe.
        /// </summary>
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

        /// <summary>
        /// O processo pai e o "dono" do filho? Steam: a janela e do steamwebhelper.exe, que mora numa
        /// subpasta do steam.exe e e filho dele. So sobe quando o filho esta DENTRO da pasta do pai
        /// (mesmo produto instalado) e o pai nao e do Windows (explorer, svchost...), para um programa
        /// iniciado pelo Explorer nunca virar "Windows Explorer".
        /// </summary>
        public static bool IsOwnerOf(string? parentPath, string? childPath, string windowsDir)
        {
            if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(childPath)) return false;
            if (string.Equals(parentPath, childPath, StringComparison.OrdinalIgnoreCase)) return false;
            if (IsUnder(parentPath, windowsDir)) return false;
            var parentDir = System.IO.Path.GetDirectoryName(parentPath);
            var childDir = System.IO.Path.GetDirectoryName(childPath);
            if (string.IsNullOrEmpty(parentDir) || string.IsNullOrEmpty(childDir)) return false;
            // Pai na raiz de um drive (C:\x.exe) seria dono de tudo no disco.
            if (System.IO.Path.GetDirectoryName(parentDir) == null) return false;
            return IsUnder(childDir + "\\x", parentDir);
        }

        private static bool IsUnder(string path, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            var d = dir.TrimEnd('\\') + "\\";
            return path.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Janela que representa um aplicativo do usuario (a decisao que nao depende do Win32).</summary>
        public static bool IsUserFacing(string processName, string windowClass)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            if (Hidden.Contains(processName)) return false;
            if (HiddenClasses.Contains(windowClass)) return false;
            // O Explorer so conta nas janelas de pasta (CabinetWClass), nunca na barra e na area de trabalho.
            if (processName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                return windowClass == "CabinetWClass";
            return true;
        }
    }
}
