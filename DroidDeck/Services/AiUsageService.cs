using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Lib;

namespace DroidDeck.Services
{
    public class AiUsageService
    {
        public const string Claude = "claude";
        public const string Codex = "codex";

        private const string ClaudeUsageUrl = "https://api.anthropic.com/api/oauth/usage";
        private static readonly TimeSpan PlanCache = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan Keep = TimeSpan.FromDays(8);

        private readonly IHttpClientFactory _http;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private (DateTime at, Plan plan)? _claudePlan;
        private DateTime _claudeRetryAt = DateTime.MinValue;
        private TimeSpan _claudeBackoff = TimeSpan.Zero;
        private L.Text? _claudeError;
        private readonly LogScanner _claudeLogs = new(ParseClaudeLine, "\"usage\"");
        private readonly LogScanner _codexLogs = new(ParseCodexLine, "\"token_count\"");

        public AiUsageService(IHttpClientFactory http) => _http = http;

        public record Window(double Percent, DateTimeOffset? ResetsAt);

        public record Plan(Window? Session, Window? Week, IReadOnlyList<ModelWindow>? Models = null);

        public record ModelWindow(string Model, Window Window);

        public record Tokens(long Today, long Week);

        public record Entry(DateTimeOffset At, long Tokens, string? Id, Plan? Plan = null);

        public static string ClaudeDir =>
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } d
                ? d
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

        public static string CodexDir =>
            Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } d
                ? d
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

        public async Task<Plan> PlanAsync(string provider)
        {
            if (provider == Codex)
            {
                var plan = await Task.Run(() => _codexLogs.LatestPlan(Path.Combine(CodexDir, "sessions")));
                return plan ?? throw new LocalizedException("No Codex logs on this PC.");
            }

            await _lock.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;
                if (_claudePlan is { } c && now - c.at < PlanCache) return c.plan;
                if (now < _claudeRetryAt)
                    return _claudePlan?.plan ?? throw new LocalizedException(_claudeError!);
                try
                {
                    var plan = await FetchClaudePlanAsync();
                    _claudePlan = (now, plan);
                    _claudeBackoff = TimeSpan.Zero;
                    _claudeError = null;
                    return plan;
                }
                catch (PlanBusyException busy)
                {
                    _claudeBackoff = busy.RetryAfter
                        ?? (_claudeBackoff == TimeSpan.Zero ? PlanCache : TimeSpan.FromTicks(Math.Min(_claudeBackoff.Ticks * 2, MaxBackoff.Ticks)));
                    _claudeRetryAt = now + _claudeBackoff;
                    _claudeError = busy.Text;
                    return _claudePlan?.plan ?? throw new LocalizedException(busy.Text);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    _claudeBackoff = PlanCache;
                    _claudeRetryAt = now + PlanCache;
                    _claudeError = ex switch
                    {
                        LocalizedException le => le.Text,
                        InvalidOperationException => new L.Text(ex.Message),
                        _ => new L.Text("Anthropic didn't respond."),
                    };
                    if (ex is not InvalidOperationException && _claudePlan is { } last) return last.plan;
                    throw new LocalizedException(_claudeError);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<Plan> FetchClaudePlanAsync()
        {
            var file = Path.Combine(ClaudeDir, ".credentials.json");
            if (!File.Exists(file))
                throw new LocalizedException("Sign in to Claude Code on this PC to see the limits.");

            string? token;
            long expires;
            using (var creds = JsonDocument.Parse(await File.ReadAllTextAsync(file)))
            {
                if (!creds.RootElement.TryGetProperty("claudeAiOauth", out var o))
                    throw new LocalizedException("Sign in to Claude Code on this PC to see the limits.");
                token = o.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
                expires = o.TryGetProperty("expiresAt", out var e) && e.TryGetInt64(out var ms) ? ms : 0;
            }
            if (string.IsNullOrEmpty(token))
                throw new LocalizedException("Sign in to Claude Code on this PC to see the limits.");
            if (expires > 0 && DateTimeOffset.FromUnixTimeMilliseconds(expires) < DateTimeOffset.UtcNow)
                throw new LocalizedException("The Claude Code login expired. Open Claude Code to renew it.");

            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var req = new HttpRequestMessage(HttpMethod.Get, ClaudeUsageUrl);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
            req.Headers.UserAgent.ParseAdd("DroidDeck");
            using var resp = await client.SendAsync(req);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new LocalizedException("The Claude Code login expired. Open Claude Code to renew it.");
            if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
            {
                var wait = resp.Headers.RetryAfter?.Delta
                    ?? (resp.Headers.RetryAfter?.Date is { } d ? d - DateTimeOffset.UtcNow : null);
                throw new PlanBusyException(new L.Text("Anthropic asked for a break; retrying soon."),
                    wait is { } w && w > TimeSpan.Zero ? (w < MaxBackoff ? w : MaxBackoff) : null);
            }
            if (!resp.IsSuccessStatusCode)
                throw new LocalizedException("Anthropic didn't respond ({code}).", ("code", (int)resp.StatusCode));
            return ParseClaudePlan(await resp.Content.ReadAsStringAsync());
        }

        private sealed class PlanBusyException : LocalizedException
        {
            public TimeSpan? RetryAfter { get; }
            public PlanBusyException(L.Text text, TimeSpan? retryAfter) : base(text) => RetryAfter = retryAfter;
        }

        public static Plan ParseClaudePlan(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (ParseClaudeLimits(doc.RootElement) is { } listed) return listed;
            Window? Read(string name)
            {
                if (!doc.RootElement.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
                if (!w.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) return null;
                DateTimeOffset? resets = w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                    ? at : null;
                return new Window(u.GetDouble(), resets);
            }
            return new Plan(Read("five_hour"), Read("seven_day"));
        }

        private static Plan? ParseClaudeLimits(JsonElement root)
        {
            if (!root.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array) return null;
            static JsonElement Prop(JsonElement e, string name) =>
                e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
            static string? Str(JsonElement e, string name) =>
                Prop(e, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

            Window? session = null, week = null;
            var models = new List<ModelWindow>();
            foreach (var l in limits.EnumerateArray())
            {
                var percent = Prop(l, "percent");
                if (percent.ValueKind != JsonValueKind.Number) continue;
                DateTimeOffset? resets = DateTimeOffset.TryParse(Str(l, "resets_at"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var at) ? at : null;
                var window = new Window(percent.GetDouble(), resets);
                switch (Str(l, "kind"))
                {
                    case "session": session = window; break;
                    case "weekly_all": week = window; break;
                    case "weekly_scoped":
                        models.Add(new ModelWindow(Str(Prop(Prop(l, "scope"), "model"), "display_name") ?? "model", window));
                        break;
                }
            }
            if (session == null && week == null && models.Count == 0) return null;
            return new Plan(session, week, models.Count > 0 ? models : null);
        }

        public Task<Tokens> TokensAsync(string provider) => Task.Run(() =>
        {
            var entries = provider == Codex
                ? _codexLogs.Entries(Path.Combine(CodexDir, "sessions"))
                : _claudeLogs.Entries(Path.Combine(ClaudeDir, "projects"));
            return Sum(entries, DateTimeOffset.Now);
        });

        public static Tokens Sum(IEnumerable<Entry> entries, DateTimeOffset now)
        {
            var today = new DateTimeOffset(now.Date, now.Offset);
            var week = today.AddDays(-6);
            long t = 0, w = 0;
            foreach (var e in entries)
            {
                if (e.At >= week && e.At <= now.AddMinutes(5)) w += e.Tokens;
                if (e.At >= today && e.At <= now.AddMinutes(5)) t += e.Tokens;
            }
            return new Tokens(t, w);
        }

        public static Entry? ParseClaudeLine(string line)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return null;
            if (!msg.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("timestamp", out var ts) || !DateTimeOffset.TryParse(ts.GetString(),
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return null;
            long Num(string k) => u.TryGetProperty(k, out var v) && v.TryGetInt64(out var n) ? n : 0;
            var tokens = Num("input_tokens") + Num("output_tokens")
                + Num("cache_creation_input_tokens") + Num("cache_read_input_tokens");
            var id = msg.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (root.TryGetProperty("requestId", out var rq)) id += "|" + rq.GetString();
            return new Entry(at, tokens, id);
        }

        public static Entry? ParseCodexLine(string line)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) return null;
            if (!p.TryGetProperty("type", out var type) || type.GetString() != "token_count") return null;
            if (!root.TryGetProperty("timestamp", out var ts) || !DateTimeOffset.TryParse(ts.GetString(),
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return null;

            long tokens = 0;
            if (p.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object
                && info.TryGetProperty("last_token_usage", out var last) && last.ValueKind == JsonValueKind.Object)
            {
                if (last.TryGetProperty("total_tokens", out var tt) && tt.TryGetInt64(out var n)) tokens = n;
                else
                {
                    long Num(string k) => last.TryGetProperty(k, out var v) && v.TryGetInt64(out var x) ? x : 0;
                    tokens = Num("input_tokens") + Num("output_tokens");
                }
            }

            Plan? plan = null;
            if (p.TryGetProperty("rate_limits", out var rl) && rl.ValueKind == JsonValueKind.Object)
            {
                Window? Read(string name)
                {
                    if (!rl.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
                    if (!w.TryGetProperty("used_percent", out var u) || u.ValueKind != JsonValueKind.Number) return null;
                    DateTimeOffset? resets = null;
                    if (w.TryGetProperty("resets_at", out var r) && r.TryGetInt64(out var unix))
                        resets = DateTimeOffset.FromUnixTimeSeconds(unix);
                    else if (w.TryGetProperty("resets_in_seconds", out var s) && s.TryGetInt64(out var secs))
                        resets = at.AddSeconds(secs);
                    return new Window(u.GetDouble(), resets);
                }
                var primary = Read("primary");
                var secondary = Read("secondary");
                if (primary != null || secondary != null) plan = new Plan(primary, secondary);
            }
            return new Entry(at, tokens, null, plan);
        }

        public sealed class LogScanner
        {
            private readonly Func<string, Entry?> _parse;
            private readonly string _marker;
            private readonly object _gate = new();
            private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);

            private sealed class FileState
            {
                public long Offset;
                public readonly List<Entry> Entries = new();
                public readonly HashSet<string> Ids = new();
            }

            public LogScanner(Func<string, Entry?> parse, string marker)
            {
                _parse = parse;
                _marker = marker;
            }

            public List<Entry> Entries(string dir)
            {
                lock (_gate)
                {
                    Scan(dir);
                    var cutoff = DateTimeOffset.UtcNow - Keep;
                    return _files.Values.SelectMany(f => f.Entries).Where(e => e.At >= cutoff).ToList();
                }
            }

            public Plan? LatestPlan(string dir)
            {
                lock (_gate)
                {
                    Scan(dir);
                    return _files.Values.SelectMany(f => f.Entries)
                        .Where(e => e.Plan != null)
                        .OrderByDescending(e => e.At)
                        .Select(e => e.Plan)
                        .FirstOrDefault();
                }
            }

            private void Scan(string dir)
            {
                if (!Directory.Exists(dir)) return;
                var since = DateTime.UtcNow - Keep;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories))
                {
                    FileInfo fi;
                    try { fi = new FileInfo(path); }
                    catch { continue; }
                    if (fi.LastWriteTimeUtc < since) continue;
                    seen.Add(path);
                    if (!_files.TryGetValue(path, out var st)) _files[path] = st = new FileState();
                    if (fi.Length < st.Offset)
                    {
                        st.Offset = 0;
                        st.Entries.Clear();
                        st.Ids.Clear();
                    }
                    if (fi.Length == st.Offset) continue;
                    try { ReadFrom(path, st); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                foreach (var gone in _files.Keys.Where(k => !seen.Contains(k)).ToList())
                    _files.Remove(gone);
            }

            private void ReadFrom(string path, FileState st)
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(st.Offset, SeekOrigin.Begin);
                var marker = Encoding.UTF8.GetBytes(_marker);
                var chunk = new byte[1 << 20];
                var line = new MemoryStream();
                int n;
                while ((n = fs.Read(chunk, 0, chunk.Length)) > 0)
                {
                    var start = 0;
                    for (var i = 0; i < n; i++)
                    {
                        if (chunk[i] != (byte)'\n') continue;
                        line.Write(chunk, start, i - start);
                        st.Offset += line.Length + 1;
                        Handle(line, marker, st);
                        line.SetLength(0);
                        start = i + 1;
                    }
                    line.Write(chunk, start, n - start);
                }
            }

            private void Handle(MemoryStream line, byte[] marker, FileState st)
            {
                var bytes = new ReadOnlySpan<byte>(line.GetBuffer(), 0, (int)line.Length);
                if (bytes.IndexOf(marker) < 0) return;
                Entry? e;
                try { e = _parse(Encoding.UTF8.GetString(bytes)); }
                catch (JsonException) { return; }
                catch (FormatException) { return; }
                catch (InvalidOperationException) { return; }
                if (e == null) return;
                if (e.Id != null && !st.Ids.Add(e.Id)) return;
                st.Entries.Add(e);
            }
        }
    }
}
