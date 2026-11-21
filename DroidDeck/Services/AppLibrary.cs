using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using DroidDeck.Lib;

namespace DroidDeck.Services
{
    public class AppEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Group { get; set; } = "";
        public bool Favorite { get; set; }
    }

    /// <summary>
    /// Biblioteca de aplicativos do KZ Companion: nome, caminho (.exe/.lnk), grupo, favorito e
    /// o icone extraido do proprio programa. Vive em %AppData%\KZCompanion\apps.json; os icones,
    /// em app-icons\{id}.png. O celular so lista e abre por id: o caminho nunca vem dele.
    /// </summary>
    public sealed class AppLibrary
    {
        private static readonly string[] AllowedExtensions = { ".exe", ".lnk" };
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
        private readonly object _gate = new();

        private static string FilePath => System.IO.Path.Combine(AppPaths.RoamingDir, "apps.json");
        private static string IconDir
        {
            get
            {
                var d = System.IO.Path.Combine(AppPaths.RoamingDir, "app-icons");
                Directory.CreateDirectory(d);
                return d;
            }
        }
        private static string IconFile(string id) => System.IO.Path.Combine(IconDir, id + ".png");

        public List<AppEntry> List()
        {
            lock (_gate) return Load();
        }

        public AppEntry? Get(string id)
        {
            lock (_gate) return Load().FirstOrDefault(a => a.Id == id);
        }

        public string? IconBase64(string id)
        {
            try
            {
                var f = IconFile(id);
                return File.Exists(f) ? Convert.ToBase64String(File.ReadAllBytes(f)) : null;
            }
            catch { return null; }
        }

        /// <summary>Valida o caminho escolhido. Devolve o caminho completo ou o motivo da recusa.</summary>
        public static bool TryValidatePath(string? path, out string full, out string error)
        {
            full = "";
            error = "";
            if (string.IsNullOrWhiteSpace(path)) { error = "Caminho vazio."; return false; }
            try { full = System.IO.Path.GetFullPath(path.Trim().Trim('"')); }
            catch { error = "Caminho inválido."; return false; }
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) { error = "Caminhos de rede não são aceitos."; return false; }
            var ext = System.IO.Path.GetExtension(full);
            if (!AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) { error = "Escolha um arquivo .exe ou .lnk."; return false; }
            if (!File.Exists(full)) { error = "Arquivo não encontrado."; return false; }
            return true;
        }

        public AppEntry Add(AppEntry entry, byte[]? icon)
        {
            lock (_gate)
            {
                var list = Load();
                entry.Id = Guid.NewGuid().ToString("N");
                list.Add(entry);
                Save(list);
            }
            SaveIcon(entry.Id, icon ?? ExtractIconPng(entry.Path));
            return entry;
        }

        public AppEntry? Update(string id, AppEntry changes, byte[]? icon)
        {
            AppEntry? found;
            lock (_gate)
            {
                var list = Load();
                found = list.FirstOrDefault(a => a.Id == id);
                if (found == null) return null;
                found.Name = changes.Name;
                found.Group = changes.Group;
                found.Favorite = changes.Favorite;
                if (!string.IsNullOrEmpty(changes.Path)) found.Path = changes.Path;
                Save(list);
            }
            if (icon != null) SaveIcon(id, icon);
            return found;
        }

        public bool Remove(string id)
        {
            lock (_gate)
            {
                var list = Load();
                var n = list.RemoveAll(a => a.Id == id);
                if (n == 0) return false;
                Save(list);
            }
            try { File.Delete(IconFile(id)); } catch { }
            return true;
        }

        private static List<AppEntry> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(FilePath), Json) ?? new();
            }
            catch (Exception ex) { Log.Warning($"apps.json ilegível: {ex.Message}"); }
            return new();
        }

        private static void Save(List<AppEntry> list)
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }

        private static void SaveIcon(string id, byte[]? png)
        {
            if (png == null) return;
            try { File.WriteAllBytes(IconFile(id), png); } catch (Exception ex) { Log.Warning($"Ícone não salvo: {ex.Message}"); }
        }

        /// <summary>Normaliza uma imagem enviada pelo usuario para PNG 96x96 (recusa o que nao for imagem).</summary>
        public static byte[]? NormalizeUserIcon(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return null;
            try
            {
                var raw = Convert.FromBase64String(base64);
                if (raw.Length > 4 * 1024 * 1024) return null;
                using var ms = new MemoryStream(raw);
                using var img = Image.FromStream(ms);
                return ToPng(img);
            }
            catch { return null; }
        }

        private static byte[] ToPng(Image source)
        {
            const int size = 96;
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(source, 0, 0, size, size);
            }
            using var o = new MemoryStream();
            bmp.Save(o, ImageFormat.Png);
            return o.ToArray();
        }

        // ---- Extracao de icone ----

        /// <summary>
        /// Icone do programa em PNG 96x96. Para .lnk usa o do alvo (sem a seta do atalho);
        /// cai no do proprio atalho quando o alvo nao e um arquivo (apps da Loja).
        /// </summary>
        public static byte[]? ExtractIconPng(string path)
        {
            try
            {
                var source = path;
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var target = ResolveShortcutTarget(path);
                    if (!string.IsNullOrEmpty(target) && File.Exists(target)) source = target;
                }
                using var icon = LoadShellIcon(source, jumbo: true) ?? LoadShellIcon(source, jumbo: false)
                                 ?? Icon.ExtractAssociatedIcon(source);
                if (icon == null) return null;
                using var bmp = icon.ToBitmap();
                return ToPng(bmp);
            }
            catch (Exception ex)
            {
                Log.Warning($"Ícone de '{path}' não extraído: {ex.Message}");
                return null;
            }
        }

        public static string? ResolveShortcutTarget(string lnk)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                dynamic shell = Activator.CreateInstance(t)!;
                dynamic sc = shell.CreateShortcut(lnk);
                string target = sc.TargetPath;
                Marshal.FinalReleaseComObject(sc);
                Marshal.FinalReleaseComObject(shell);
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            catch { return null; }
        }

        private static Icon? LoadShellIcon(string file, bool jumbo)
        {
            var info = new SHFILEINFO();
            var r = SHGetFileInfo(file, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX);
            if (r == IntPtr.Zero) return null;
            var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList
            if (SHGetImageList(jumbo ? SHIL_JUMBO : SHIL_EXTRALARGE, ref iid, out var list) != 0 || list == null) return null;
            try
            {
                if (list.GetIcon(info.iIcon, ILD_TRANSPARENT, out var hIcon) != 0 || hIcon == IntPtr.Zero) return null;
                try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
                finally { DestroyIcon(hIcon); }
            }
            finally { Marshal.ReleaseComObject(list); }
        }

        private const uint SHGFI_SYSICONINDEX = 0x4000;
        private const int SHIL_EXTRALARGE = 2;
        private const int SHIL_JUMBO = 4;
        private const int ILD_TRANSPARENT = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", EntryPoint = "#727")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        // ---- Aplicativos instalados (atalhos do Menu Iniciar) ----

        public static List<(string Name, string Path)> InstalledShortcuts()
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            };
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList(); }
                catch { continue; }
                foreach (var f in files)
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("desinstal", StringComparison.OrdinalIgnoreCase)) continue;
                    seen.TryAdd(name, f);
                }
            }
            return seen.OrderBy(k => k.Key, StringComparer.CurrentCultureIgnoreCase)
                       .Select(k => (k.Key, k.Value)).ToList();
        }
    }
}
