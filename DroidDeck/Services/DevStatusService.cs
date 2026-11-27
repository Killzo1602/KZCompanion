using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DroidDeck.Services
{
    public class DevStatusService
    {
        public record Session(string Id, string Project, string Status, DateTimeOffset? Since);

        public static List<Session> ClaudeSessions(string claudeDir, Func<int, bool>? alive = null)
        {
            var dir = Path.Combine(claudeDir, "sessions");
            var list = new List<Session>();
            if (!Directory.Exists(dir)) return list;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var doc = JsonDocument.Parse(stream);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("pid", out var p) || !p.TryGetInt32(out var pid) || !(alive ?? IsClaude)(pid)) continue;
                    var status = Str(root, "status") switch
                    {
                        "busy" or "shell" => "busy",
                        "waiting" => "waiting",
                        _ => "idle",
                    };
                    DateTimeOffset? since = root.TryGetProperty("statusUpdatedAt", out var at) && at.TryGetInt64(out var ms)
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
                    var cwd = (Str(root, "cwd") ?? "").TrimEnd('\\', '/');
                    var project = cwd[(cwd.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
                    list.Add(new Session(Str(root, "sessionId") ?? pid.ToString(),
                        project.Length == 0 ? "Claude Code" : project, status, since));
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
                {
                }
            }
            return list.OrderBy(s => s.Status == "waiting" ? 0 : s.Status == "busy" ? 1 : 2).ThenBy(s => s.Project).ToList();
        }

        private static bool IsClaude(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return p.ProcessName.Equals("claude", StringComparison.OrdinalIgnoreCase)
                    || p.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return false;
            }
        }

        public record Check(string Repo, string Branch, string State, string Title, DateTimeOffset? At);
        public record GitHub(bool Installed, string? Error, List<Check> Repos, List<Check> PullRequests);

        private const string Query = @"query {
  viewer {
    repositories(first: 8, orderBy: {field: PUSHED_AT, direction: DESC}, ownerAffiliations: [OWNER, COLLABORATOR, ORGANIZATION_MEMBER]) {
      nodes { name defaultBranchRef { name target { ... on Commit { messageHeadline committedDate statusCheckRollup { state } } } } }
    }
    pullRequests(first: 8, states: OPEN, orderBy: {field: UPDATED_AT, direction: DESC}) {
      nodes { title number updatedAt repository { name } commits(last: 1) { nodes { commit { statusCheckRollup { state } } } } }
    }
  }
}";

        private static readonly TimeSpan GitHubCache = TimeSpan.FromSeconds(60);
        private readonly SemaphoreSlim _gitHubLock = new(1, 1);
        private (DateTime at, GitHub value)? _gitHub;

        public async Task<GitHub> GitHubAsync(CancellationToken ct)
        {
            await _gitHubLock.WaitAsync(ct);
            try
            {
                if (_gitHub is { } c && DateTime.UtcNow - c.at < GitHubCache) return c.value;
                var value = await ReadGitHubAsync(ct);
                _gitHub = (DateTime.UtcNow, value);
                return value;
            }
            finally { _gitHubLock.Release(); }
        }

        private static async Task<GitHub> ReadGitHubAsync(CancellationToken ct)
        {
            if (FindTool("gh") is not { } gh) return new GitHub(false, null, new(), new());
            try
            {
                var (code, output, _) = await RunAsync(gh, new[] { "api", "graphql", "-f", "query=" + Query }, TimeSpan.FromSeconds(20), ct);
                return code == 0
                    ? ParseGitHub(output)
                    : new GitHub(true, "GitHub did not answer. Run 'gh auth status' on the PC.", new(), new());
            }
            catch (Exception ex) when (ex is TimeoutException or JsonException or System.ComponentModel.Win32Exception)
            {
                Lib.Log.Warning($"GitHub: {ex.Message}");
                return new GitHub(true, "GitHub did not answer.", new(), new());
            }
        }

        public static GitHub ParseGitHub(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var viewer = Prop(Prop(doc.RootElement, "data"), "viewer");
            var repos = new List<Check>();
            foreach (var r in Nodes(Prop(viewer, "repositories")))
            {
                var branch = Prop(r, "defaultBranchRef");
                var commit = Prop(branch, "target");
                if (commit.ValueKind != JsonValueKind.Object) continue;
                repos.Add(new Check(Str(r, "name") ?? "", Str(branch, "name") ?? "", State(commit),
                    Str(commit, "messageHeadline") ?? "", Date(commit, "committedDate")));
            }
            var prs = new List<Check>();
            foreach (var p in Nodes(Prop(viewer, "pullRequests")))
            {
                var last = Nodes(Prop(p, "commits")).FirstOrDefault();
                var number = Prop(p, "number");
                prs.Add(new Check(Str(Prop(p, "repository"), "name") ?? "",
                    number.ValueKind == JsonValueKind.Number ? "#" + number.GetInt32() : "",
                    State(Prop(last, "commit")), Str(p, "title") ?? "", Date(p, "updatedAt")));
            }
            return new GitHub(true, null, repos, prs);
        }

        private static string State(JsonElement commit) => Str(Prop(commit, "statusCheckRollup"), "state") switch
        {
            "SUCCESS" => "success",
            "FAILURE" or "ERROR" => "failure",
            "PENDING" or "EXPECTED" => "pending",
            _ => "none",
        };

        public record Container(string Id, string Name, string Image, bool Running, string Status);
        public record Docker(bool Installed, string? Error, List<Container> Containers);

        private static readonly Regex ContainerId = new("^[0-9a-f]{12,64}$", RegexOptions.Compiled);

        public async Task<Docker> DockerAsync(CancellationToken ct)
        {
            if (FindTool("docker") is not { } docker) return new Docker(false, null, new());
            try
            {
                var (code, output, _) = await RunAsync(docker, new[] { "ps", "-a", "--format", "{{json .}}" }, TimeSpan.FromSeconds(10), ct);
                return code == 0
                    ? new Docker(true, null, ParseContainers(output))
                    : new Docker(true, "Docker is not running.", new());
            }
            catch (Exception ex) when (ex is TimeoutException or System.ComponentModel.Win32Exception)
            {
                return new Docker(true, "Docker did not answer.", new());
            }
        }

        public static List<Container> ParseContainers(string output)
        {
            var list = new List<Container>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    list.Add(new Container(Str(r, "ID") ?? "", Str(r, "Names") ?? "", Str(r, "Image") ?? "",
                        Str(r, "State") == "running", Str(r, "Status") ?? ""));
                }
                catch (JsonException) { }
            }
            return list.OrderByDescending(c => c.Running).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<(bool Found, string? Error)> DockerActionAsync(string id, string action, CancellationToken ct)
        {
            if (action is not ("start" or "stop" or "restart")) return (true, "Unknown action.");
            if (!ContainerId.IsMatch(id)) return (false, null);
            var list = await DockerAsync(ct);
            if (list.Containers.All(c => c.Id != id)) return (false, null);
            try
            {
                var (code, _, error) = await RunAsync(FindTool("docker")!, new[] { action, id }, TimeSpan.FromSeconds(40), ct);
                Lib.Log.Info($"docker {action} {id}: {(code == 0 ? "ok" : error.Trim())}");
                return (true, code == 0 ? null : "Docker refused the command.");
            }
            catch (Exception ex) when (ex is TimeoutException or System.ComponentModel.Win32Exception)
            {
                return (true, "Docker did not answer.");
            }
        }

        public static string? FindTool(string name) =>
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => Path.Combine(d.Trim('"', ' '), name + ".exe"))
                .FirstOrDefault(File.Exists);

        private static async Task<(int Code, string Output, string Error)> RunAsync(string exe, IEnumerable<string> args,
            TimeSpan limit, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new TimeoutException(exe);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(limit);
            try
            {
                var output = p.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = p.StandardError.ReadToEndAsync(timeout.Token);
                await p.WaitForExitAsync(timeout.Token);
                return (p.ExitCode, await output, await error);
            }
            catch (OperationCanceledException)
            {
                try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { }
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException(Path.GetFileName(exe));
            }
        }

        private static JsonElement Prop(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

        private static string? Str(JsonElement e, string name) =>
            Prop(e, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

        private static DateTimeOffset? Date(JsonElement e, string name) =>
            DateTimeOffset.TryParse(Str(e, name), out var d) ? d : null;

        private static IEnumerable<JsonElement> Nodes(JsonElement connection) =>
            Prop(connection, "nodes") is { ValueKind: JsonValueKind.Array } n ? n.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
    }
}
