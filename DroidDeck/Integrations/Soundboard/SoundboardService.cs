using DroidDeck.Lib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DroidDeck.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DroidDeck.Services
{
    public class SoundboardConfig
    {
        public string? CableDeviceId { get; set; }

        public string? MonitorDeviceId { get; set; }

        public bool MonitorEnabled { get; set; } = false;

        public int Volume { get; set; } = 100;
    }

    public class SoundResult
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Mp3 { get; set; } = "";
    }

    public class SoundboardService : Integrations.IIntegration
    {
        public string Name => "soundboard";
        public string Title => "Soundboard";

        public bool IsConnected => true;

        private const string ApiBase = "https://myinstants-api.vercel.app";

        private readonly ILogger<SoundboardService> _logger;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IHubContext<DeckHub> _hub;

        private readonly object _lock = new();
        private readonly List<ActivePlayback> _active = new();

        public string? NowPlayingTitle { get; private set; }

        private sealed class ActivePlayback
        {
            public required IWavePlayer Output { get; init; }
            public required WaveStream Reader { get; init; }

            public MMDevice? Device { get; init; }
        }

        public SoundboardService(
            ILogger<SoundboardService> logger,
            IHttpClientFactory httpFactory,
            IHubContext<DeckHub> hub)
        {
            _logger = logger;
            _httpFactory = httpFactory;
            _hub = hub;
        }

        private static string BaseDir
        {
            get
            {
                var dir = AppPaths.LocalDir;
                return dir;
            }
        }

        private static string ConfigPath => Path.Combine(BaseDir, "soundboard.json");

        private static string CacheDir
        {
            get
            {
                var dir = Path.Combine(BaseDir, "SoundCache");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public SoundboardConfig LoadConfig()
        {
            try { if (File.Exists(ConfigPath)) return JsonSerializer.Deserialize<SoundboardConfig>(File.ReadAllText(ConfigPath)) ?? new SoundboardConfig(); }
            catch { }
            return new SoundboardConfig();
        }

        public void SaveConfig(SoundboardConfig c)
        {
            try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(c)); } catch { }
        }

        public async Task<List<SoundResult>> SearchAsync(string query)
        {
            return await FetchAsync($"{ApiBase}/search?q={Uri.EscapeDataString(query ?? "")}");
        }

        public async Task<List<SoundResult>> TrendingAsync()
        {
            return await FetchAsync($"{ApiBase}/trending?q=us");
        }

        private async Task<List<SoundResult>> FetchAsync(string url)
        {
            var results = new List<SoundResult>();
            try
            {
                var http = _httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                var json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        var mp3 = GetStr(item, "mp3");
                        if (string.IsNullOrWhiteSpace(mp3)) continue;
                        results.Add(new SoundResult
                        {
                            Id = GetStr(item, "id"),
                            Title = GetStr(item, "title"),
                            Mp3 = mp3,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao buscar sons no MyInstants ({Url})", url);
            }
            return results;
        }

        private static string GetStr(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

        private static bool IsSafeId(string? id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128) return false;
            foreach (var c in id)
                if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')) return false;
            return true;
        }

        internal const long MaxSoundBytes = 5L * 1024 * 1024;
        internal const long MaxCacheBytes = 200L * 1024 * 1024;

        internal static string CacheFileFor(string id, string url)
        {
            var name = IsSafeId(id)
                ? id
                : "u-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32].ToLowerInvariant();
            return Path.Combine(CacheDir, name + ".mp3");
        }

        private async Task<string> EnsureCachedAsync(string id, string url)
        {
            var path = CacheFileFor(id, url);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }
                return path;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Invalid sound URL (http/https only).");

            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxSoundBytes)
                throw new InvalidOperationException("Sound file is too large.");

            var tmp = path + ".tmp";
            try
            {
                await using (var source = await response.Content.ReadAsStreamAsync())
                await using (var target = File.Create(tmp))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer)) > 0)
                    {
                        total += read;
                        if (total > MaxSoundBytes)
                            throw new InvalidOperationException("Sound file is too large.");
                        await target.WriteAsync(buffer.AsMemory(0, read));
                    }
                }
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }

            PruneCache(CacheDir, MaxCacheBytes, keep: path);
            return path;
        }

        internal static void PruneCache(string dir, long maxBytes, string? keep = null)
        {
            try
            {
                var files = new DirectoryInfo(dir).GetFiles("*.mp3")
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToList();
                var total = files.Sum(f => f.Length);
                foreach (var f in files)
                {
                    if (total <= maxBytes) break;
                    if (keep != null && string.Equals(f.FullName, Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                        continue;
                    var length = f.Length;
                    try { f.Delete(); total -= length; } catch { }
                }
            }
            catch
            {
            }
        }

        public async Task PlayAsync(string id, string url, string? title)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                _logger.LogWarning("PlayAsync sem url (id={Id})", id);
                return;
            }

            string path;
            try { path = await EnsureCachedAsync(id, url); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao baixar/cachear o som {Url}", url);
                return;
            }

            StopAll(broadcast: false);

            var cfg = LoadConfig();
            float vol = Math.Clamp(cfg.Volume, 0, 100) / 100f;

            var targets = new List<MMDevice?>();
            targets.Add(ResolveDevice(cfg.CableDeviceId));
            if (cfg.MonitorEnabled && !string.IsNullOrWhiteSpace(cfg.MonitorDeviceId))
            {
                var monitor = ResolveDevice(cfg.MonitorDeviceId);
                if (monitor != null) targets.Add(monitor);
            }

            lock (_lock)
            {
                foreach (var device in targets)
                {
                    ActivePlayback? playback = null;
                    try
                    {
                        var reader = new MediaFoundationReader(path);
                        var sample = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = vol };
                        IWavePlayer output = device != null
                            ? new WasapiOut(device, AudioClientShareMode.Shared, false, 200)
                            : new WasapiOut(AudioClientShareMode.Shared, 200);
                        output.Init(sample);
                        playback = new ActivePlayback { Output = output, Reader = reader, Device = device };
                        output.PlaybackStopped += (_, __) => OnPlaybackStopped(playback);
                        output.Play();
                        _active.Add(playback);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Falha ao iniciar playback no device {Device}", device?.FriendlyName ?? "(padrão)");
                        if (playback == null) { try { device?.Dispose(); } catch { } }
                    }
                }
                NowPlayingTitle = _active.Count > 0 ? (title ?? "") : null;
            }

            await BroadcastAsync();
        }

        private MMDevice? ResolveDevice(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            try
            {
                using var en = new MMDeviceEnumerator();
                return en.GetDevice(id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dispositivo de saída {Id} não encontrado; usando padrão", id);
                return null;
            }
        }

        private void OnPlaybackStopped(ActivePlayback pb)
        {
            bool becameEmpty = false;
            lock (_lock)
            {
                if (_active.Remove(pb))
                {
                    try { pb.Output.Dispose(); } catch { }
                    try { pb.Reader.Dispose(); } catch { }
                    try { pb.Device?.Dispose(); } catch { }
                }
                if (_active.Count == 0)
                {
                    NowPlayingTitle = null;
                    becameEmpty = true;
                }
            }
            if (becameEmpty) _ = BroadcastAsync();
        }

        public void StopAll(bool broadcast = true)
        {
            List<ActivePlayback> toStop;
            lock (_lock)
            {
                toStop = _active.ToList();
                _active.Clear();
                NowPlayingTitle = null;
            }
            foreach (var pb in toStop)
            {
                try { pb.Output.Stop(); } catch { }
                try { pb.Output.Dispose(); } catch { }
                try { pb.Reader.Dispose(); } catch { }
                try { pb.Device?.Dispose(); } catch { }
            }
            if (broadcast) _ = BroadcastAsync();
        }

        public List<object> GetOutputDevices()
        {
            var list = new List<object>();
            try
            {
                using var en = new MMDeviceEnumerator();
                foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    using (d)
                    {
                        list.Add(new { id = d.ID, name = d.FriendlyName });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enumerar dispositivos de saída");
            }
            return list;
        }

        public object GetStatePayload() => new
        {
            playing = NowPlayingTitle != null,
            title = NowPlayingTitle,
        };

        private async Task BroadcastAsync()
        {
            try { await _hub.Clients.All.SendAsync("ReceivePlaybackState", GetStatePayload()); }
            catch { }
        }
    }
}
