using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        /// <summary>Icone trocado pelo usuario (nao e regenerado na migracao do extrator).</summary>
        public bool CustomIcon { get; set; }
        /// <summary>"file" (.lnk/.exe) ou "packaged" (app da Loja, abre pelo AppUserModelID em AppId).</summary>
        public string Kind { get; set; } = "file";
        public string AppId { get; set; } = "";
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

        public AppLibrary() => Task.Run(MigrateIcons);

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

        /// <summary>
        /// Identidade canonica de um programa: caminho completo do alvo (em minusculas) + argumentos.
        /// "--processStart X.exe" apontando para o proprio alvo e ruido (o atalho do Discord que passa
        /// isso e o que nao passa abrem o mesmo programa); argumentos que mudam o programa (ex.:
        /// Update.exe --processStart Slack.exe) continuam diferenciando.
        /// </summary>
        public static string IdentityOf(string target, string? args)
        {
            var full = target;
            try { full = System.IO.Path.GetFullPath(target); } catch { }
            full = full.ToLowerInvariant();
            var a = (args ?? "").Trim().ToLowerInvariant();
            // Marcador que o Chrome/Edge colocam nos atalhos do Menu Iniciar: nao muda o programa aberto.
            a = Regex.Replace(a, @"--source-shortcut-location=\S+", "").Trim();
            var m = Regex.Match(a, @"^--process-?start\s+""?([^""\s]+)""?$");
            if (m.Success && m.Groups[1].Value == System.IO.Path.GetFileName(full)) a = "";
            return a.Length == 0 ? full : full + "|" + a;
        }

        /// <summary>Identidade de um caminho escolhido: .lnk resolve para alvo + argumentos.</summary>
        public static string IdentityOfPath(string path)
        {
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var (t, a) = ResolveShortcut(path);
                if (!string.IsNullOrEmpty(t)) return IdentityOf(t, a);
            }
            return IdentityOf(path, "");
        }

        public static string IdentityOfEntry(AppEntry e) =>
            e.Kind == "packaged" ? "pkg:" + e.AppId.ToLowerInvariant()
            : e.Kind is "steam" or "battlenet" ? e.Kind + ":" + e.AppId.ToLowerInvariant()
            : IdentityOfPath(e.Path);

        /// <summary>Identidades de tudo que ja esta na biblioteca (para o "ja adicionado" do seletor).</summary>
        public HashSet<string> Identities()
        {
            lock (_gate) return Load().Select(IdentityOfEntry).ToHashSet();
        }

        public AppEntry? FindByIdentity(string identity, string? exceptId = null)
        {
            lock (_gate) return Load().FirstOrDefault(a => a.Id != exceptId && IdentityOfEntry(a) == identity);
        }

        /// <summary>Ja existe um aplicativo com o mesmo caminho ou a mesma identidade? (ex.: o .exe e o atalho dele)</summary>
        public AppEntry? FindDuplicate(string fullPath, string? exceptId = null)
        {
            var id = IdentityOfPath(fullPath);
            lock (_gate)
                return Load().FirstOrDefault(a => a.Id != exceptId &&
                    (string.Equals(a.Path, fullPath, StringComparison.OrdinalIgnoreCase) || IdentityOfEntry(a) == id));
        }

        /// <summary>Define a ordem (a mesma lista guardada no arquivo): ids na ordem pedida primeiro; os demais seguem depois, na ordem atual.</summary>
        public void Reorder(IReadOnlyList<string> ids)
        {
            lock (_gate)
            {
                var list = Load();
                var byId = list.ToDictionary(a => a.Id);
                var next = new List<AppEntry>();
                foreach (var id in ids)
                    if (byId.Remove(id, out var a)) next.Add(a);
                next.AddRange(list.Where(a => byId.ContainsKey(a.Id)));
                Save(next);
            }
        }

        /// <summary>Extrator novo (128 px, contain): refaz os icones automaticos salvos pela versao anterior, uma vez.</summary>
        public void MigrateIcons()
        {
            const string version = "3";
            try
            {
                var marker = System.IO.Path.Combine(IconDir, ".version");
                if (File.Exists(marker) && File.ReadAllText(marker).Trim() == version) return;
                List<AppEntry> list;
                lock (_gate) list = Load();
                foreach (var a in list.Where(a => !a.CustomIcon))
                {
                    if (a.Kind == "packaged") { SaveIcon(a.Id, InstalledApps.ExtractPackagedIconPng(a.AppId)); continue; }
                    if (a.Kind is "steam" or "battlenet") continue; // arte local do jogo ja salva ao adicionar
                    if (!File.Exists(a.Path)) continue;
                    SaveIcon(a.Id, ExtractIconPng(a.Path));
                }
                File.WriteAllText(marker, version);
            }
            catch (Exception ex) { Log.Warning($"Migração de ícones falhou: {ex.Message}"); }
        }

        /// <param name="customIcon">true so quando o usuario escolheu a imagem (nao e regenerada na migracao).</param>
        public AppEntry Add(AppEntry entry, byte[]? icon, bool customIcon = false)
        {
            lock (_gate)
            {
                var list = Load();
                entry.Id = Guid.NewGuid().ToString("N");
                entry.CustomIcon = customIcon;
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
                if (icon != null) found.CustomIcon = true;
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

        // Quadro final: 128x128 com 8 px de folga. O icone NUNCA e cortado nem esticado:
        // recorta so a margem transparente, escala mantendo a proporcao (contain) e centraliza.
        private const int IconSize = 128;
        private const int IconPad = 8;

        /// <summary>Encaixa qualquer imagem no quadro 128x128 (contain, sem cortar), como os icones de apps.</summary>
        public static byte[] FitToPng(Image source) => ToPng(source);

        private static byte[] ToPng(Image source)
        {
            using var src = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g0 = Graphics.FromImage(src))
            {
                g0.Clear(Color.Transparent);
                g0.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            var box = ContentBounds(src);
            var inner = IconSize - 2 * IconPad;
            // Sem ampliar demais: icone pequeno (ex.: 48 px) no maximo 2x.
            var scale = Math.Min(Math.Min(inner / (double)box.Width, inner / (double)box.Height), 2.0);
            var w = Math.Max(1, (int)Math.Round(box.Width * scale));
            var h = Math.Max(1, (int)Math.Round(box.Height * scale));

            using var bmp = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(src, new Rectangle((IconSize - w) / 2, (IconSize - h) / 2, w, h), box, GraphicsUnit.Pixel);
            }
            using var o = new MemoryStream();
            bmp.Save(o, ImageFormat.Png);
            return o.ToArray();
        }

        /// <summary>Retangulo com pixels visiveis (alfa > 8). Imagem toda opaca/transparente devolve a imagem inteira.</summary>
        private static Rectangle ContentBounds(Bitmap bmp)
        {
            var full = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
                var row = new byte[Math.Abs(data.Stride)];
                for (int y = 0; y < bmp.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, Math.Abs(data.Stride));
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        if (row[x * 4 + 3] <= 8) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
                return maxX < 0 ? full : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            }
            finally { bmp.UnlockBits(data); }
        }

        // ---- Extracao de icone ----

        /// <summary>
        /// Icone do programa em PNG 128x128 (contain, sem cortar). Para .lnk usa o do alvo (sem a seta do
        /// atalho); cai no do proprio atalho quando o alvo nao e um arquivo (apps da Loja). Tenta o maior
        /// icone do proprio arquivo (ate 256 px, inclusive PNG) e so depois a lista de icones do shell,
        /// que para exes sem 256 px entrega o icone pequeno solto num canto da tela (o "cortado").
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
                using var icon = ExtractResourceIcon(source) ?? LoadShellIcon(source, jumbo: true)
                                 ?? LoadShellIcon(source, jumbo: false) ?? Icon.ExtractAssociatedIcon(source);
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

        /// <summary>Maior icone dos recursos do .exe/.dll (256, 128, 64, 48, 32), ou null.</summary>
        private static Icon? ExtractResourceIcon(string file)
        {
            foreach (var size in new[] { 256, 128, 64, 48, 32 })
            {
                var handles = new IntPtr[1];
                var ids = new uint[1];
                uint n;
                try { n = PrivateExtractIcons(file, 0, size, size, handles, ids, 1, 0); }
                catch { return null; }
                if (n == 0 || n == 0xFFFFFFFF || handles[0] == IntPtr.Zero) continue;
                try { return (Icon)Icon.FromHandle(handles[0]).Clone(); }
                finally { DestroyIcon(handles[0]); }
            }
            return null;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);

        /// <summary>Alvo e argumentos de um .lnk (vazios se nao resolver).</summary>
        public static (string Target, string Args) ResolveShortcut(string lnk)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return ("", "");
                dynamic shell = Activator.CreateInstance(t)!;
                dynamic sc = shell.CreateShortcut(lnk);
                string target = sc.TargetPath;
                string args = sc.Arguments;
                Marshal.FinalReleaseComObject(sc);
                Marshal.FinalReleaseComObject(shell);
                return (target ?? "", args ?? "");
            }
            catch { return ("", ""); }
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
    }
}
