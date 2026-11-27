using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DroidDeck.Lib;
using Microsoft.Win32;

namespace DroidDeck.Services
{
    public sealed class InstalledItem
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Kind { get; init; } = "file";
        public string Path { get; init; } = "";
        public string Identity { get; init; } = "";
        public string Source { get; init; } = "";
        public string Detail { get; set; } = "";
        public GameInfo? Game { get; init; }
    }

    public sealed class InstalledApps
    {
        private static readonly Regex Aumid = new(@"^[A-Za-z0-9][A-Za-z0-9.\-]*_[a-z0-9]{13}![A-Za-z0-9._\-]+$", RegexOptions.Compiled);

        private readonly object _gate = new();
        private DateTime _at = DateTime.MinValue;
        private Dictionary<string, InstalledItem> _items = new();
        private readonly ConcurrentDictionary<string, string?> _icons = new();

        public static bool IsValidAumid(string? id) => id != null && id.Length < 256 && Aumid.IsMatch(id);

        public List<InstalledItem> List(bool refresh = false)
        {
            lock (_gate)
            {
                if (refresh || DateTime.UtcNow - _at > TimeSpan.FromSeconds(90) || _items.Count == 0)
                {
                    _items = Enumerate().ToDictionary(i => i.Key);
                    _at = DateTime.UtcNow;
                    _icons.Clear();
                }
                return _items.Values.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        public InstalledItem? Find(string key)
        {
            List();
            lock (_gate) return _items.TryGetValue(key, out var i) ? i : null;
        }

        public string? IconBase64(InstalledItem item)
        {
            if (_icons.TryGetValue(item.Key, out var cached)) return cached;
            byte[]? png = IconFor(item);
            var b64 = png == null ? null : Convert.ToBase64String(png);
            _icons[item.Key] = b64;
            return b64;
        }

        public static byte[]? IconFor(InstalledItem item) => item.Kind switch
        {
            "packaged" => ExtractPackagedIconPng(item.Path),
            "steam" => item.Game == null ? null : SteamGames.IconPng(item.Game),
            "battlenet" => item.Game == null ? null : SteamGames.ExeIconPng(item.Game.InstallDir),
            _ => AppLibrary.ExtractIconPng(item.Path),
        };

        private static string Key(string identity) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(identity)))[..12].ToLowerInvariant();

        private static List<InstalledItem> Enumerate()
        {
            var byIdentity = new Dictionary<string, InstalledItem>();

            var candidates = new List<(string Name, string Lnk, string Identity, bool HasArgs)>();
            foreach (var root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            }.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList(); }
                catch { continue; }
                foreach (var f in files)
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("desinstal", StringComparison.OrdinalIgnoreCase)) continue;
                    var (target, args) = AppLibrary.ResolveShortcut(f);
                    if (string.IsNullOrEmpty(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target)) continue;
                    if (System.IO.Path.GetFileName(target).Contains("unins", StringComparison.OrdinalIgnoreCase)) continue;
                    candidates.Add((name, f, AppLibrary.IdentityOf(target, args), !string.IsNullOrWhiteSpace(args)));
                }
            }
            foreach (var g in candidates.GroupBy(c => c.Identity))
            {
                var rep = g.OrderBy(c => c.HasArgs).ThenBy(c => c.Lnk.Length).First();
                byIdentity[g.Key] = new InstalledItem
                {
                    Key = Key(g.Key), Name = rep.Name, Kind = "file", Path = rep.Lnk, Identity = g.Key, Source = "Start Menu",
                    Detail = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(rep.Lnk) ?? "") is { Length: > 0 } d && d != "Programs" ? d : "",
                };
            }

            foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var ap = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                    if (ap == null) continue;
                    foreach (var sub in ap.GetSubKeyNames().Where(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        using var k = ap.OpenSubKey(sub);
                        var path = (k?.GetValue("") as string)?.Trim().Trim('"');
                        if (string.IsNullOrEmpty(path)) continue;
                        path = Environment.ExpandEnvironmentVariables(path);
                        if (!File.Exists(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) continue;
                        var identity = AppLibrary.IdentityOf(path, "");
                        if (byIdentity.ContainsKey(identity)) continue;
                        string name;
                        try { name = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? ""; } catch { name = ""; }
                        if (name.Length == 0) name = System.IO.Path.GetFileNameWithoutExtension(path);
                        byIdentity[identity] = new InstalledItem { Key = Key(identity), Name = name, Kind = "file", Path = path, Identity = identity, Source = "Installed programs" };
                    }
                }
                catch { }
            }

            foreach (var (name, id) in EnumeratePackaged())
            {
                var identity = "pkg:" + id.ToLowerInvariant();
                byIdentity[identity] = new InstalledItem { Key = Key(identity), Name = name, Kind = "packaged", Path = id, Identity = identity, Source = "Microsoft Store" };
            }

            foreach (var g in SteamGames.Enumerate().Concat(BattleNetGames.Enumerate()))
            {
                var prefix = g.InstallDir.TrimEnd('\\').ToLowerInvariant() + "\\";
                foreach (var dup in byIdentity.Where(kv => kv.Value.Kind == "file" && kv.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(kv => kv.Key).ToList())
                    byIdentity.Remove(dup);
                var identity = g.Kind + ":" + g.Id.ToLowerInvariant();
                byIdentity[identity] = new InstalledItem
                {
                    Key = Key(identity), Name = g.Name, Kind = g.Kind, Path = g.Id, Identity = identity,
                    Source = g.Kind == "steam" ? "Steam" : "Battle.net", Game = g,
                };
            }

            var list = byIdentity.Values.ToList();
            foreach (var g in list.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var i in g) if (i.Detail.Length == 0) i.Detail = i.Source;
            return list;
        }

        private static List<(string Name, string Id)> EnumeratePackaged()
        {
            var result = new List<(string, string)>();
            var t = new Thread(() =>
            {
                try
                {
                    var type = Type.GetTypeFromProgID("Shell.Application");
                    if (type == null) return;
                    dynamic shell = Activator.CreateInstance(type)!;
                    dynamic folder = shell.NameSpace("shell:AppsFolder");
                    foreach (dynamic item in folder.Items())
                    {
                        string id = item.Path;
                        string name = item.Name;
                        if (IsValidAumid(id) && !string.IsNullOrWhiteSpace(name)) result.Add((name.Trim(), id));
                    }
                }
                catch (Exception ex) { Log.Warning($"Apps empacotados: {ex.Message}"); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            if (!t.Join(TimeSpan.FromSeconds(20))) Log.Warning("Apps empacotados: tempo esgotado");
            return result;
        }

        public static byte[]? ExtractPackagedIconPng(string aumid)
        {
            if (!IsValidAumid(aumid)) return null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName("shell:AppsFolder\\" + aumid, IntPtr.Zero, ref iid, out var factory);
                if (factory.GetImage(new SIZE { cx = 256, cy = 256 }, 0x4  , out hbm) != 0 || hbm == IntPtr.Zero) return null;
                using var bmp = ArgbFromHBitmap(hbm);
                return bmp == null ? null : AppLibrary.FitToPng(bmp);
            }
            catch (Exception ex)
            {
                Log.Warning($"Icone de '{aumid}' nao extraido: {ex.Message}");
                return null;
            }
            finally { if (hbm != IntPtr.Zero) DeleteObject(hbm); }
        }

        private static Bitmap? ArgbFromHBitmap(IntPtr hbm)
        {
            if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var info) == 0) return null;
            if (info.bmBitsPixel == 32 && info.bmBits != IntPtr.Zero)
            {
                using var shared = new Bitmap(info.bmWidth, info.bmHeight, info.bmWidthBytes, PixelFormat.Format32bppPArgb, info.bmBits);
                var copy = new Bitmap(info.bmWidth, info.bmHeight, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(copy);
                g.Clear(Color.Transparent);
                g.DrawImage(shared, 0, 0, info.bmWidth, info.bmHeight);
                return copy;
            }
            return Image.FromHbitmap(hbm) is Bitmap b ? b : null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public ushort bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out BITMAP bmp);
    }
}
