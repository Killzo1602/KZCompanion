using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Lib;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Actions.Providers
{
    public class ScriptProvider : IActionProvider
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

        private readonly ILogger<ScriptProvider> _logger;

        public ScriptProvider(ILogger<ScriptProvider> logger) => _logger = logger;

        public string Type => "script";

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Script",
            Icon = "code",
            Category = "Utilities",
            Description = "Runs a PowerShell or Python script or a .bat file, with arguments.",
            Fields =
            {
                ActionField.Select("source", "Script",
                    ("inline", "Written here"),
                    ("file", "A file on the PC")),
                ActionField.Select("language", "Language",
                    ("powershell", "PowerShell"),
                    ("python", "Python"),
                    ("cmd", "Command Prompt (.bat)"))
                    .When("source", "inline"),
                ActionField.Multiline("code", "Code", "Write-Output \"Hello, $($args[0])\"",
                    "Arguments arrive in $args (PowerShell), sys.argv (Python) or %1, %2… (.bat).")
                    .When("source", "inline"),
                ActionField.Text("path", "File", @"C:\scripts\backup.ps1",
                    ".ps1, .py, .bat or .cmd.")
                    .When("source", "file"),
                ActionField.Text("arguments", "Arguments (optional)", "\"my game\" 2",
                    "Separated by spaces; use quotes for an argument with spaces."),
            },
        };

        public async Task ExecuteAsync(ActionContext c)
        {
            string file;
            if (c.Get("source", "inline") == "file")
            {
                file = c.Get("path").Trim().Trim('"');
                if (!File.Exists(file))
                {
                    _logger.LogWarning("Script: arquivo '{File}' nao existe", file);
                    return;
                }
            }
            else
            {
                var code = c.Get("code");
                if (string.IsNullOrWhiteSpace(code)) return;
                file = Materialize(c.Get("language", "powershell"), code);
            }

            var psi = StartInfo(file, c.Get("arguments"));
            if (psi == null)
            {
                _logger.LogWarning("Script: tipo de arquivo nao suportado '{File}'", file);
                return;
            }

            using var proc = Process.Start(psi);
            if (proc == null) return;
            using var cts = new CancellationTokenSource(Timeout);
            var stdout = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = proc.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
                var err = (await stderr).Trim();
                if (proc.ExitCode != 0)
                    _logger.LogWarning("Script terminou com codigo {Code}: {Error}", proc.ExitCode, Truncate(err));
                else
                    _logger.LogDebug("Script: {Output}", Truncate((await stdout).Trim()));
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                _logger.LogWarning("Script passou de {Minutes} minutos e foi encerrado", Timeout.TotalMinutes);
            }
        }

        public static string Materialize(string language, string code)
        {
            var ext = language switch { "python" => ".py", "cmd" => ".bat", _ => ".ps1" };
            var dir = Path.Combine(AppPaths.LocalDir, "scripts");
            Directory.CreateDirectory(dir);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(language + "\n" + code)))[..16];
            var file = Path.Combine(dir, hash + ext);
            if (!File.Exists(file))
            {
                var encoding = ext == ".ps1" ? new UTF8Encoding(true) : new UTF8Encoding(false);
                File.WriteAllText(file, code.Replace("\r\n", "\n").Replace("\n", "\r\n"), encoding);
            }
            return file;
        }

        public static ProcessStartInfo? StartInfo(string file, string arguments)
        {
            var args = SplitArguments(arguments);
            ProcessStartInfo psi;
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".ps1":
                    psi = new ProcessStartInfo("powershell.exe");
                    foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file })
                        psi.ArgumentList.Add(a);
                    break;
                case ".py":
                case ".pyw":
                    psi = new ProcessStartInfo(FindOnPath("py.exe") ?? "python.exe");
                    psi.ArgumentList.Add(file);
                    break;
                case ".bat":
                case ".cmd":
                    psi = new ProcessStartInfo("cmd.exe");
                    psi.ArgumentList.Add("/d");
                    psi.ArgumentList.Add("/c");
                    psi.ArgumentList.Add(file);
                    break;
                default:
                    return null;
            }
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WorkingDirectory = Path.GetDirectoryName(file) ?? AppPaths.LocalDir;
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            return psi;
        }

        public static List<string> SplitArguments(string text)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool quoted = false, any = false;
            foreach (var ch in text ?? "")
            {
                if (ch == '"') { quoted = !quoted; any = true; continue; }
                if (char.IsWhiteSpace(ch) && !quoted)
                {
                    if (any) result.Add(current.ToString());
                    current.Clear();
                    any = false;
                    continue;
                }
                current.Append(ch);
                any = true;
            }
            if (any) result.Add(current.ToString());
            return result;
        }

        private static string? FindOnPath(string exe)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    var full = Path.Combine(dir.Trim('"'), exe);
                    if (File.Exists(full)) return full;
                }
                catch (ArgumentException) { }
            }
            var windows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), exe);
            return File.Exists(windows) ? windows : null;
        }

        private static string Truncate(string s) => s.Length > 500 ? s[..500] + "…" : s;
    }
}
