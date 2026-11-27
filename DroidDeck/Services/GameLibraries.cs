using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DroidDeck.Lib;
using Microsoft.Win32;

namespace DroidDeck.Services
{
    public sealed class GameInfo
    {
        public string Kind { get; init; } = "";
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string InstallDir { get; init; } = "";
        public string Launcher { get; init; } = "";
    }

    public static class Vdf
    {
        public sealed class Node
        {
            public string? Value;
            public Dictionary<string, Node> Children = new(StringComparer.OrdinalIgnoreCase);
            public Node? this[string key] => Children.TryGetValue(key, out var n) ? n : null;
        }

        public static Node Parse(string text)
        {
            var pos = 0;
            var root = new Node();
            ParseBlock(text, ref pos, root, top: true);
            return root;
        }

        private static void ParseBlock(string s, ref int i, Node into, bool top)
        {
            while (true)
            {
                var key = NextToken(s, ref i, out var isBrace);
                if (key == null) return;
                if (isBrace) { if (key == "}") return; continue; }
                var value = NextToken(s, ref i, out var valueBrace);
                if (value == null) return;
                var child = new Node();
                if (valueBrace && value == "{") ParseBlock(s, ref i, child, top: false);
                else child.Value = value;
                into.Children[key] = child;
            }
        }

        private static string? NextToken(string s, ref int i, out bool brace)
        {
            brace = false;
            while (i < s.Length)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                break;
            }
            if (i >= s.Length) return null;
            if (s[i] == '{' || s[i] == '}') { brace = true; return s[i++].ToString(); }
            if (s[i] != '"') { var st = i; while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}') i++; return s[st..i]; }
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', '\\' => '\\', '"' => '"', _ => s[i] });
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }
    }

    public static class SteamGames
    {
        private static readonly HashSet<string> NotGames = new() { "228980", "1070560", "1391110", "1493710", "1826330", "1628350", "2180100", "1113280", "2805730", "3658110" };
        private static readonly Regex ToolName = new(@"^(Steamworks Common|Steam Linux Runtime|Proton\b|Steam Controller|SteamVR\b|Steam Client)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string? InstallPath()
        {
            foreach (var p in new[]
            {
                Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
                Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
                Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string,
                @"C:\Program Files (x86)\Steam",
            })
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try { var full = Path.GetFullPath(p); if (Directory.Exists(Path.Combine(full, "steamapps"))) return full; } catch { }
            }
            return null;
        }

        public static List<string> Libraries(string steam)
        {
            var libs = new List<string> { steam };
            try
            {
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    var root = Vdf.Parse(File.ReadAllText(vdf, Encoding.UTF8));
                    var folders = root["libraryfolders"];
                    if (folders != null)
                        foreach (var f in folders.Children.Values)
                        {
                            var path = f.Value ?? f["path"]?.Value;
                            if (!string.IsNullOrWhiteSpace(path)) libs.Add(path);
                        }
                }
            }
            catch (Exception ex) { Log.Warning($"Steam: libraryfolders.vdf ilegivel: {ex.Message}"); }
            var result = new List<string>();
            foreach (var l in libs)
            {
                try { var full = Path.GetFullPath(l); if (Directory.Exists(Path.Combine(full, "steamapps")) && !result.Contains(full, StringComparer.OrdinalIgnoreCase)) result.Add(full); } catch { }
            }
            return result;
        }

        public static List<GameInfo> Enumerate()
        {
            var games = new Dictionary<string, GameInfo>();
            var steam = InstallPath();
            if (steam == null) return new();
            foreach (var lib in Libraries(steam))
            {
                var apps = Path.Combine(lib, "steamapps");
                IEnumerable<string> manifests;
                try { manifests = Directory.EnumerateFiles(apps, "appmanifest_*.acf").ToList(); } catch { continue; }
                foreach (var m in manifests)
                {
                    try
                    {
                        var state = Vdf.Parse(File.ReadAllText(m, Encoding.UTF8))["AppState"];
                        if (state == null) continue;
                        var id = state["appid"]?.Value ?? "";
                        var name = state["name"]?.Value ?? "";
                        var dir = state["installdir"]?.Value ?? "";
                        if (!Regex.IsMatch(id, @"^\d{1,10}$") || name.Length == 0) continue;
                        if (NotGames.Contains(id) || ToolName.IsMatch(name)) continue;
                        if (!int.TryParse(state["StateFlags"]?.Value, out var flags) || (flags & 4) == 0) continue;
                        var installDir = dir.Length > 0 ? Path.Combine(apps, "common", dir) : "";
                        if (installDir.Length == 0 || !Directory.Exists(installDir)) continue;
                        games.TryAdd(id, new GameInfo { Kind = "steam", Id = id, Name = CleanName(name), InstallDir = installDir });
                    }
                    catch (Exception ex) { Log.Warning($"Steam: {Path.GetFileName(m)} ilegivel: {ex.Message}"); }
                }
            }
            return games.Values.ToList();
        }

        public static string CleanName(string n) =>
            n.Replace("\u00C2\u00AE", "").Replace("\u00AE", "").Replace("\u2122", "").Trim();

        public static byte[]? IconPng(GameInfo g)
        {
            var steam = InstallPath();
            if (steam != null)
            {
                var dir = Path.Combine(steam, "appcache", "librarycache", g.Id);
                if (Directory.Exists(dir))
                {
                    string[] names = { "library_600x900.jpg", "library_capsule.jpg", "library_600x900.png", "library_capsule.png" };
                    var all = SafeFiles(dir);
                    foreach (var pattern in new[] { names, new[] { "icon" }, new[] { "logo.png", "library_header.jpg", "header.jpg" } })
                    {
                        string? file = null;
                        if (pattern.Length == 1 && pattern[0] == "icon")
                            file = Directory.EnumerateFiles(dir, "*.jpg", SearchOption.TopDirectoryOnly)
                                .FirstOrDefault(f => Regex.IsMatch(Path.GetFileNameWithoutExtension(f), "^[0-9a-f]{40}$"));
                        else
                            file = all.FirstOrDefault(f => pattern.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
                        if (file != null && TryFit(file) is { } png) return png;
                    }
                }
            }
            return ExeIconPng(g.InstallDir);
        }

        private static List<string> SafeFiles(string dir)
        {
            try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Take(200).ToList(); } catch { return new(); }
        }

        private static byte[]? TryFit(string file)
        {
            try
            {
                using var img = Image.FromFile(file);
                return AppLibrary.FitToPng(img);
            }
            catch { return null; }
        }

        public static byte[]? ExeIconPng(string installDir)
        {
            try
            {
                if (!Directory.Exists(installDir)) return null;
                var skip = new Regex(@"unins|crash|handler|redist|setup|vc_?redist|dxsetup|easyanticheat|beservice|battleye|launcher_helper|report|updater", RegexOptions.IgnoreCase);
                var exe = new DirectoryInfo(installDir).EnumerateFiles("*.exe", SearchOption.TopDirectoryOnly)
                    .Where(f => !skip.IsMatch(f.Name)).OrderByDescending(f => f.Length).FirstOrDefault();
                return exe == null ? null : AppLibrary.ExtractIconPng(exe.FullName);
            }
            catch { return null; }
        }
    }

    public static class BattleNetGames
    {
        private static readonly Dictionary<string, (string Code, string Name)> Known = new(StringComparer.OrdinalIgnoreCase)
        {
            ["hs_beta"] = ("WTCG", "Hearthstone"), ["wow"] = ("WoW", "World of Warcraft"),
            ["wow_classic"] = ("WoWC", "World of Warcraft Classic"), ["wow_classic_era"] = ("WoW_classic", "World of Warcraft Classic Era"),
            ["d3"] = ("D3", "Diablo III"), ["s2"] = ("S2", "StarCraft II"), ["s1"] = ("S1", "StarCraft"),
            ["heroes"] = ("Hero", "Heroes of the Storm"), ["prometheus"] = ("Pro", "Overwatch 2"),
            ["odin"] = ("ODIN", "Call of Duty: Warzone"), ["fenris"] = ("Fen", "Call of Duty: Modern Warfare"),
            ["viper"] = ("VIPR", "Call of Duty: Black Ops 4"), ["zeus"] = ("ZEUS", "Call of Duty: Black Ops Cold War"),
            ["lazarus"] = ("LAZR", "Call of Duty: Modern Warfare II"), ["w3"] = ("W3", "Warcraft III: Reforged"),
        };

        public static List<GameInfo> Enumerate()
        {
            var result = new List<GameInfo>();
            try
            {
                var db = @"C:\ProgramData\Battle.net\Agent\product.db";
                if (!File.Exists(db)) return result;
                byte[] bytes;
                using (var fs = new FileStream(db, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var ms = new MemoryStream()) { fs.CopyTo(ms); bytes = ms.ToArray(); }
                var products = ReadProducts(bytes);
                var launcher = products.Where(p => p.Uid.Equals("battle.net", StringComparison.OrdinalIgnoreCase))
                    .Select(p => Path.Combine(p.Path, "Battle.net.exe")).FirstOrDefault(File.Exists)
                    ?? new[] { @"C:\Program Files (x86)\Battle.net\Battle.net.exe", @"C:\Program Files\Battle.net\Battle.net.exe" }.FirstOrDefault(File.Exists);
                if (launcher == null) return result;
                foreach (var p in products)
                {
                    if (!Known.TryGetValue(p.Uid, out var k)) continue;
                    if (p.Path.Length == 0 || !Directory.Exists(p.Path)) continue;
                    result.Add(new GameInfo { Kind = "battlenet", Id = k.Code, Name = k.Name, InstallDir = p.Path, Launcher = launcher });
                }
            }
            catch (Exception ex) { Log.Warning($"Battle.net: {ex.Message}"); }
            return result.GroupBy(g => g.Id).Select(g => g.First()).ToList();
        }

        public static string? FindLauncher() => Enumerate().Select(g => g.Launcher).FirstOrDefault()
            ?? new[] { @"C:\Program Files (x86)\Battle.net\Battle.net.exe", @"C:\Program Files\Battle.net\Battle.net.exe" }.FirstOrDefault(File.Exists);

        private sealed record Product(string Uid, string Path);

        private static List<Product> ReadProducts(byte[] data)
        {
            var list = new List<Product>();
            foreach (var (field, payload) in Fields(data, 0, data.Length))
            {
                if (field != 1 || payload == null) continue;
                var strings = new List<(int Field, string Text)>();
                Collect(payload, 0, 0, strings);
                var uid = strings.FirstOrDefault(s => s.Field == 1).Text ?? "";
                var path = strings.Select(s => s.Text).FirstOrDefault(t => Regex.IsMatch(t, @"^[A-Za-z]:[\\/]")) ?? "";
                if (uid.Length > 0) list.Add(new Product(uid, path.Replace('/', '\\')));
            }
            return list;
        }

        private static void Collect(byte[] b, int depth, int topField, List<(int, string)> into)
        {
            foreach (var (field, payload) in Fields(b, 0, b.Length))
            {
                if (payload == null) continue;
                var text = Encoding.UTF8.GetString(payload);
                var printable = payload.Length > 0 && payload.All(c => c >= 32 && c < 127 || c >= 0xC2);
                if (printable && !LooksNested(payload)) { into.Add((depth == 0 ? field : topField, text)); continue; }
                if (depth < 4) Collect(payload, depth + 1, depth == 0 ? field : topField, into);
            }
        }

        private static bool LooksNested(byte[] p)
        {
            var s = Encoding.UTF8.GetString(p);
            return !Regex.IsMatch(s, @"^[ -~]+$") && Fields(p, 0, p.Length).Any();
        }

        private static IEnumerable<(int Field, byte[]? Payload)> Fields(byte[] b, int start, int end)
        {
            var i = start;
            var res = new List<(int, byte[]?)>();
            while (i < end)
            {
                if (!Varint(b, ref i, end, out var tag)) break;
                var wt = (int)(tag & 7);
                var field = (int)(tag >> 3);
                if (field == 0) break;
                if (wt == 0) { if (!Varint(b, ref i, end, out _)) break; res.Add((field, null)); }
                else if (wt == 2)
                {
                    if (!Varint(b, ref i, end, out var len) || len > (ulong)(end - i)) break;
                    res.Add((field, b[i..(i + (int)len)])); i += (int)len;
                }
                else if (wt == 1) { i += 8; res.Add((field, null)); }
                else if (wt == 5) { i += 4; res.Add((field, null)); }
                else break;
            }
            return res;
        }

        private static bool Varint(byte[] b, ref int i, int end, out ulong v)
        {
            v = 0;
            for (var shift = 0; shift < 64 && i < end; shift += 7)
            {
                var x = b[i++];
                v |= (ulong)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) return true;
            }
            return false;
        }
    }
}
