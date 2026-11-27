using DroidDeck.Lib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Services
{
    public class DiscordConfig
    {
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }

    public class DiscordRpcService : Integrations.IIntegration
    {
        private const string RedirectUri = "http://localhost:4787/discord";
        private static readonly string[] Scopes = { "rpc", "rpc.voice.read", "rpc.voice.write" };

        private readonly ILogger<DiscordRpcService> _logger;
        private readonly IHubContext<DeckHub> _hub;

        private NamedPipeClientStream? _pipe;
        private CancellationTokenSource? _readCts;
        private TaskCompletionSource<bool>? _readyTcs;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        public bool Connected { get; private set; }

        public string Name => "discord";
        public string Title => "Discord";
        public bool IsConnected => Connected;
        public bool SelfMute { get; private set; }
        public bool SelfDeaf { get; private set; }
        public string? VoiceChannelId { get; private set; }
        public string? VoiceChannelName { get; private set; }
        public string? VoiceGuildId { get; private set; }
        public int InputVolume { get; private set; }
        public int OutputVolume { get; private set; }
        public string VoiceMode { get; private set; } = "";
        private volatile List<object> _participants = new();

        public IReadOnlyList<object> Participants => _participants;
        private readonly ConcurrentDictionary<string, bool> _userMute = new();

        public bool Configured
        {
            get
            {
                var c = LoadConfig();
                return !string.IsNullOrWhiteSpace(c.ClientId) &&
                       !string.IsNullOrWhiteSpace(c.ClientSecret);
            }
        }

        public object GetStatePayload() => new
        {
            configured = Configured,
            clientId = LoadConfig().ClientId,
            connected = Connected,
            mute = SelfMute,
            deaf = SelfDeaf,
            channelId = VoiceChannelId,
            channelName = VoiceChannelName,
            inputVolume = InputVolume,
            outputVolume = OutputVolume,
            voiceMode = VoiceMode,
            participants = _participants,
        };

        public DiscordRpcService(ILogger<DiscordRpcService> logger, IHubContext<DeckHub> hub)
        {
            _logger = logger;
            _hub = hub;
        }

        private static string ConfigPath
        {
            get
            {
                var dir = AppPaths.LocalDir;
                return Path.Combine(dir, "discord.json");
            }
        }

        private DiscordConfig? _configCache;

        public DiscordConfig LoadConfig()
        {
            if (_configCache is { } cached) return Clone(cached);
            var cfg = new DiscordConfig();
            try
            {
                if (File.Exists(ConfigPath))
                    cfg = JsonSerializer.Deserialize<DiscordConfig>(SecretFile.ReadAllText(ConfigPath)) ?? new DiscordConfig();
            }
            catch { }
            _configCache = cfg;
            return Clone(cfg);
        }

        public void SaveConfig(DiscordConfig c)
        {
            _configCache = Clone(c);
            try { SecretFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(c)); } catch { }
        }

        private static DiscordConfig Clone(DiscordConfig c) => new()
        {
            ClientId = c.ClientId,
            ClientSecret = c.ClientSecret,
            AccessToken = c.AccessToken,
            RefreshToken = c.RefreshToken,
        };

        public void SetCredentials(string clientId, string clientSecret)
        {
            var cfg = LoadConfig();
            cfg.ClientId = clientId;
            cfg.ClientSecret = clientSecret;
            cfg.AccessToken = null;
            cfg.RefreshToken = null;
            SaveConfig(cfg);
        }

        public async Task ConnectAsync(bool interactive = true)
        {
            await _connectLock.WaitAsync();
            try { await ConnectCoreAsync(interactive); }
            finally { _connectLock.Release(); }
        }

        private async Task ConnectCoreAsync(bool interactive)
        {
            var cfg = LoadConfig();
            if (string.IsNullOrWhiteSpace(cfg.ClientId) || string.IsNullOrWhiteSpace(cfg.ClientSecret))
                throw new InvalidOperationException("Set up the Discord Client ID and Secret first.");

            Disconnect();
            await ConnectPipeAsync();
            var readCts = new CancellationTokenSource();
            _readCts = readCts;
            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(async () =>
            {
                try { await ReadLoopAsync(readCts.Token); }
                finally { readCts.Dispose(); }
            });

            _logger.LogInformation("Discord: enviando handshake (client_id={Id})", cfg.ClientId);
            await WriteFrameAsync(0, new { v = 1, client_id = cfg.ClientId });
            await Task.WhenAny(_readyTcs.Task, Task.Delay(10000));
            if (!_readyTcs.Task.IsCompletedSuccessfully)
                throw new Exception("Discord didn't answer the handshake (READY). If it keeps happening, quit Discord from the tray (Quit Discord) and open it again.");

            bool authed = false;
            if (!string.IsNullOrEmpty(cfg.AccessToken))
            {
                try { await SendCommandAsync("AUTHENTICATE", new { access_token = cfg.AccessToken }); authed = true; }
                catch { authed = false; }
            }
            if (!authed && !string.IsNullOrEmpty(cfg.RefreshToken))
            {
                try
                {
                    if (await RefreshAccessTokenAsync(cfg))
                    {
                        await SendCommandAsync("AUTHENTICATE", new { access_token = cfg.AccessToken });
                        authed = true;
                    }
                }
                catch (DiscordGrantRevokedException ex)
                {
                    cfg.AccessToken = null;
                    cfg.RefreshToken = null;
                    SaveConfig(cfg);
                    _logger.LogWarning("Discord: autorização revogada ou expirada ({Msg}); é preciso reconectar pelo app", ex.Message);
                }
                catch (Exception ex) { _logger.LogWarning("Discord: renovação do token falhou: {Msg}", ex.Message); }
            }
            if (!authed)
            {
                if (!interactive)
                    throw new InvalidOperationException("The Discord authorization expired: open Settings → Discord and tap Connect.");
                await AuthorizeAndGetTokenAsync(cfg);
                await SendCommandAsync("AUTHENTICATE", new { access_token = cfg.AccessToken });
            }

            await SendCommandAsync("SUBSCRIBE", null, evt: "VOICE_SETTINGS_UPDATE");
            try { await SendCommandAsync("SUBSCRIBE", null, evt: "VOICE_CHANNEL_SELECT"); } catch { }
            var vs = await SendCommandAsync("GET_VOICE_SETTINGS", null);
            if (vs.TryGetProperty("data", out var data)) UpdateVoiceFromData(data);

            Connected = true;
            await RefreshVoiceChannelAsync();
            _logger.LogInformation("Discord RPC conectado (mute={Mute}, deaf={Deaf}, canal={Ch})",
                SelfMute, SelfDeaf, VoiceChannelName);
        }

        private async Task AuthorizeAndGetTokenAsync(DiscordConfig cfg)
        {
            var resp = await SendCommandAsync("AUTHORIZE",
                new { client_id = cfg.ClientId, scopes = Scopes }, timeoutMs: 60000);
            var code = resp.GetProperty("data").GetProperty("code").GetString()!;

            await ExchangeTokenAsync(cfg, new Dictionary<string, string>
            {
                ["client_id"] = cfg.ClientId!,
                ["client_secret"] = cfg.ClientSecret!,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
            });
        }

        private Task<bool> RefreshAccessTokenAsync(DiscordConfig cfg) =>
            ExchangeTokenAsync(cfg, new Dictionary<string, string>
            {
                ["client_id"] = cfg.ClientId!,
                ["client_secret"] = cfg.ClientSecret!,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = cfg.RefreshToken!,
            });

        private async Task<bool> ExchangeTokenAsync(DiscordConfig cfg, Dictionary<string, string> form)
        {
            using var http = new HttpClient();
            var tokenResp = await http.PostAsync("https://discord.com/api/oauth2/token",
                new FormUrlEncodedContent(form));
            var body = await tokenResp.Content.ReadAsStringAsync();
            if (!tokenResp.IsSuccessStatusCode)
            {
                if ((int)tokenResp.StatusCode == 400 && body.Contains("invalid_grant"))
                    throw new DiscordGrantRevokedException(body);
                throw new Exception($"Troca de token falhou ({(int)tokenResp.StatusCode}): {body}");
            }

            var root = JsonDocument.Parse(body).RootElement;
            cfg.AccessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            if (root.TryGetProperty("refresh_token", out var rt)) cfg.RefreshToken = rt.GetString();
            SaveConfig(cfg);
            return !string.IsNullOrEmpty(cfg.AccessToken);
        }

        public async Task SetMuteAsync(bool? mute)
        {
            EnsureConnected();
            var previous = SelfMute;
            var target = mute ?? !SelfMute;
            SelfMute = target;
            try
            {
                var r = await SendCommandAsync("SET_VOICE_SETTINGS", new { mute = target });
                if (r.TryGetProperty("data", out var d)) UpdateVoiceFromData(d);
                else await BroadcastStateAsync();
            }
            catch
            {
                SelfMute = previous;
                throw;
            }
        }

        public async Task SetDeafAsync(bool? deaf)
        {
            EnsureConnected();
            var previous = SelfDeaf;
            var target = deaf ?? !SelfDeaf;
            SelfDeaf = target;
            try
            {
                var r = await SendCommandAsync("SET_VOICE_SETTINGS", new { deaf = target });
                if (r.TryGetProperty("data", out var d)) UpdateVoiceFromData(d);
                else await BroadcastStateAsync();
            }
            catch
            {
                SelfDeaf = previous;
                throw;
            }
        }

        public async Task<object> GetGuildsAsync()
        {
            EnsureConnected();
            var r = await SendCommandAsync("GET_GUILDS", null);
            var list = new List<object>();
            if (r.TryGetProperty("data", out var d) && d.TryGetProperty("guilds", out var gs)
                && gs.ValueKind == JsonValueKind.Array)
                foreach (var g in gs.EnumerateArray())
                    list.Add(new
                    {
                        id = g.TryGetProperty("id", out var id) ? id.GetString() : null,
                        name = g.TryGetProperty("name", out var n) ? n.GetString() : null,
                    });
            return list;
        }

        public async Task<object> GetChannelsAsync(string guildId)
        {
            EnsureConnected();
            var r = await SendCommandAsync("GET_CHANNELS", new { guild_id = guildId });
            var list = new List<object>();
            if (r.TryGetProperty("data", out var d) && d.TryGetProperty("channels", out var cs)
                && cs.ValueKind == JsonValueKind.Array)
                foreach (var c in cs.EnumerateArray())
                {
                    int type = c.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.Number
                        ? t.GetInt32() : -1;
                    if (type != 2 && type != 13) continue;
                    list.Add(new
                    {
                        id = c.TryGetProperty("id", out var id) ? id.GetString() : null,
                        name = c.TryGetProperty("name", out var n) ? n.GetString() : null,
                    });
                }
            return list;
        }

        public async Task SelectVoiceChannelAsync(string? channelId)
        {
            EnsureConnected();
            await SendCommandAsync("SELECT_VOICE_CHANNEL",
                new { channel_id = string.IsNullOrEmpty(channelId) ? null : channelId, force = true });
            await RefreshVoiceChannelAsync();
        }

        public async Task RefreshVoiceChannelAsync()
        {
            try
            {
                var r = await SendCommandAsync("GET_SELECTED_VOICE_CHANNEL", null);
                if (r.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    VoiceChannelId = d.TryGetProperty("id", out var id) ? id.GetString() : null;
                    VoiceChannelName = d.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                    VoiceGuildId = d.TryGetProperty("guild_id", out var gid) ? gid.GetString() : null;
                    var parts = new List<object>();
                    if (d.TryGetProperty("voice_states", out var vss) && vss.ValueKind == JsonValueKind.Array)
                        foreach (var v in vss.EnumerateArray())
                        {
                            if (!v.TryGetProperty("user", out var u)) continue;
                            if (u.TryGetProperty("id", out var vid) && vid.GetString() is string volId &&
                                v.TryGetProperty("volume", out var vol) && vol.ValueKind == JsonValueKind.Number)
                                _userVolume[volId] = vol.GetDouble();
                            parts.Add(new
                            {
                                id = u.TryGetProperty("id", out var uid) ? uid.GetString() : null,
                                username = u.TryGetProperty("username", out var un) ? un.GetString() : "",
                                nick = v.TryGetProperty("nick", out var nk) ? nk.GetString() : null,
                            });
                        }
                    _participants = parts;
                }
                else
                {
                    VoiceChannelId = null;
                    VoiceChannelName = null;
                    _participants = new();
                }
            }
            catch
            {
                VoiceChannelId = null;
                VoiceChannelName = null;
                _participants = new();
            }
            await BroadcastStateAsync();
        }

        public async Task SetInputVolumeAsync(double volume)
        {
            EnsureConnected();
            volume = Math.Clamp(volume, 0, 100);
            var r = await SendCommandAsync("SET_VOICE_SETTINGS", new { input = new { volume } });
            if (r.TryGetProperty("data", out var d)) UpdateVoiceFromData(d);
        }

        public async Task SetOutputVolumeAsync(double volume)
        {
            EnsureConnected();
            volume = Math.Clamp(volume, 0, 200);
            var r = await SendCommandAsync("SET_VOICE_SETTINGS", new { output = new { volume } });
            if (r.TryGetProperty("data", out var d)) UpdateVoiceFromData(d);
        }

        public Task NudgeInputVolumeAsync(double delta) => SetInputVolumeAsync(InputVolume + delta);
        public Task NudgeOutputVolumeAsync(double delta) => SetOutputVolumeAsync(OutputVolume + delta);

        public async Task SetVoiceModeAsync(string type)
        {
            EnsureConnected();
            var r = await SendCommandAsync("SET_VOICE_SETTINGS", new { mode = new { type } });
            if (r.TryGetProperty("data", out var d)) UpdateVoiceFromData(d);
        }

        public Task ToggleVoiceModeAsync() =>
            SetVoiceModeAsync(VoiceMode == "PUSH_TO_TALK" ? "VOICE_ACTIVITY" : "PUSH_TO_TALK");

        public async Task SetUserVoiceAsync(string userId, bool? mute, double? volume)
        {
            EnsureConnected();
            var args = new Dictionary<string, object?> { ["user_id"] = userId };
            if (mute.HasValue) args["mute"] = mute.Value;
            if (volume.HasValue) args["volume"] = Math.Clamp(volume.Value, 0, 200);
            await SendCommandAsync("SET_USER_VOICE_SETTINGS", args);
            if (mute.HasValue) _userMute[userId] = mute.Value;
            if (volume.HasValue) _userVolume[userId] = Math.Clamp(volume.Value, 0, 200);
        }

        public double? GetUserVolume(string userId) =>
            _userVolume.TryGetValue(userId, out var v) ? v : null;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> _userVolume = new();

        public Task ToggleUserMuteAsync(string userId) =>
            SetUserVoiceAsync(userId, !(_userMute.TryGetValue(userId, out var m) && m), null);

        public async Task<object> GetSoundboardSoundsAsync()
        {
            EnsureConnected();
            var r = await SendCommandAsync("GET_SOUNDBOARD_SOUNDS", null);
            var list = new List<object>();
            if (r.TryGetProperty("data", out var d))
            {
                JsonElement arr = d;
                if (d.ValueKind == JsonValueKind.Object)
                {
                    if (!(d.TryGetProperty("sounds", out arr) && arr.ValueKind == JsonValueKind.Array)
                        && !(d.TryGetProperty("soundboard_sounds", out arr) && arr.ValueKind == JsonValueKind.Array))
                        arr = default;
                }
                if (arr.ValueKind == JsonValueKind.Array)
                    foreach (var s in arr.EnumerateArray())
                    {
                        var g = s.TryGetProperty("guild_id", out var gv) && gv.ValueKind == JsonValueKind.String
                            ? gv.GetString() : null;
                        if (string.IsNullOrEmpty(g) || g == "0") continue;
                        list.Add(MapSound(s));
                    }
            }
            return list;
        }

        private static object MapSound(JsonElement s)
        {
            string? Str(string prop) =>
                s.TryGetProperty(prop, out var v)
                    ? (v.ValueKind == JsonValueKind.String ? v.GetString()
                        : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null)
                    : null;
            var guildId = Str("guild_id");
            if (guildId == "0") guildId = null;
            return new
            {
                soundId = Str("sound_id") ?? Str("id"),
                name = Str("name") ?? "",
                guildId,
                emojiName = Str("emoji_name"),
            };
        }

        public async Task PlaySoundboardSoundAsync(string soundId, string? guildId)
        {
            EnsureConnected();
            var args = new Dictionary<string, object?> { ["sound_id"] = soundId };
            if (!string.IsNullOrEmpty(guildId)) args["guild_id"] = guildId;
            await SendCommandAsync("PLAY_SOUNDBOARD_SOUND", args);
        }

        private void EnsureConnected()
        {
            if (!Connected || _pipe is not { IsConnected: true })
                throw new InvalidOperationException("Discord is not connected.");
        }

        private async Task ConnectPipeAsync()
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var p = new NamedPipeClientStream(".", $"discord-ipc-{i}",
                        PipeDirection.InOut, PipeOptions.Asynchronous);
                    await p.ConnectAsync(1000);
                    _pipe = p;
                    return;
                }
                catch {  }
            }
            throw new IOException("Discord pipe not found — is Discord open?");
        }

        private Task WriteFrameAsync(int opcode, object payload)
            => WriteRawFrameAsync(opcode, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

        private async Task WriteRawFrameAsync(int opcode, byte[] data)
        {
            var frame = new byte[8 + data.Length];
            BitConverter.GetBytes(opcode).CopyTo(frame, 0);
            BitConverter.GetBytes(data.Length).CopyTo(frame, 4);
            data.CopyTo(frame, 8);
            await _writeLock.WaitAsync();
            try
            {
                await _pipe!.WriteAsync(frame);
                await _pipe!.FlushAsync();
            }
            finally { _writeLock.Release(); }
        }

        private async Task<JsonElement> SendCommandAsync(string cmd, object? args, string? evt = null, int timeoutMs = 10000)
        {
            var nonce = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[nonce] = tcs;

            var payload = new Dictionary<string, object?> { ["cmd"] = cmd, ["nonce"] = nonce };
            if (args != null) payload["args"] = args;
            if (evt != null) payload["evt"] = evt;
            await WriteFrameAsync(1, payload);

            using var cts = new CancellationTokenSource(timeoutMs);
            await using var reg = cts.Token.Register(() =>
            {
                if (_pending.TryRemove(nonce, out var t))
                    t.TrySetException(new TimeoutException($"Discord '{cmd}' expirou."));
            });
            return await tcs.Task;
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            var header = new byte[8];
            try
            {
                while (!ct.IsCancellationRequested && _pipe is { IsConnected: true })
                {
                    await ReadExactAsync(header, 8, ct);
                    int opcode = BitConverter.ToInt32(header, 0);
                    int len = BitConverter.ToInt32(header, 4);
                    var buf = new byte[len];
                    await ReadExactAsync(buf, len, ct);

                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Discord IPC recv op={Op} len={Len}: {Body}",
                            opcode, len, len <= 600 ? Encoding.UTF8.GetString(buf) : "(payload grande)");

                    switch (opcode)
                    {
                        case 3:
                            await WriteRawFrameAsync(4, buf);
                            break;
                        case 2:
                            _logger.LogWarning("Discord RPC CLOSE: {Msg}", Encoding.UTF8.GetString(buf));
                            throw new IOException("Discord closed the connection (CLOSE).");
                        case 4:
                            break;
                        default:
                            HandleFrame(Encoding.UTF8.GetString(buf));
                            break;
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Discord RPC desconectado: {Msg}", ex.Message);
                Connected = false;
                foreach (var kv in _pending)
                    if (_pending.TryRemove(kv.Key, out var t))
                        t.TrySetException(new IOException("Discord desconectou."));
                await BroadcastStateAsync();
            }
        }

        private async Task ReadExactAsync(byte[] buf, int count, CancellationToken ct)
        {
            int read = 0;
            while (read < count)
            {
                int n = await _pipe!.ReadAsync(buf.AsMemory(read, count - read), ct);
                if (n <= 0) throw new IOException("Pipe closed.");
                read += n;
            }
        }

        private void HandleFrame(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("nonce", out var nonceEl) &&
                    nonceEl.ValueKind == JsonValueKind.String &&
                    _pending.TryRemove(nonceEl.GetString()!, out var tcs))
                {
                    if (root.TryGetProperty("evt", out var e) && e.GetString() == "ERROR")
                    {
                        var msg = root.TryGetProperty("data", out var d) && d.TryGetProperty("message", out var m)
                            ? m.GetString() : "erro RPC";
                        tcs.TrySetException(new Exception($"Discord: {msg}"));
                    }
                    else tcs.TrySetResult(root.Clone());
                    return;
                }

                if (root.TryGetProperty("evt", out var evtEl))
                {
                    var evt = evtEl.GetString();
                    if (evt == "READY") _readyTcs?.TrySetResult(true);
                    else if (evt == "VOICE_SETTINGS_UPDATE" && root.TryGetProperty("data", out var vd))
                        UpdateVoiceFromData(vd);
                    else if (evt == "VOICE_CHANNEL_SELECT")
                        _ = RefreshVoiceChannelAsync();
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Discord: frame inválido"); }
        }

        private void UpdateVoiceFromData(JsonElement data)
        {
            if (data.TryGetProperty("mute", out var m) && (m.ValueKind == JsonValueKind.True || m.ValueKind == JsonValueKind.False))
                SelfMute = m.GetBoolean();
            if (data.TryGetProperty("deaf", out var d) && (d.ValueKind == JsonValueKind.True || d.ValueKind == JsonValueKind.False))
                SelfDeaf = d.GetBoolean();
            if (data.TryGetProperty("input", out var inp) && inp.TryGetProperty("volume", out var iv) && iv.ValueKind == JsonValueKind.Number)
                InputVolume = (int)Math.Round(iv.GetDouble());
            if (data.TryGetProperty("output", out var outp) && outp.TryGetProperty("volume", out var ov) && ov.ValueKind == JsonValueKind.Number)
                OutputVolume = (int)Math.Round(ov.GetDouble());
            if (data.TryGetProperty("mode", out var mode) && mode.TryGetProperty("type", out var mt) && mt.ValueKind == JsonValueKind.String)
                VoiceMode = mt.GetString() ?? "";
            _ = BroadcastStateAsync();
        }

        private async Task BroadcastStateAsync()
        {
            try
            {
                await _hub.Clients.All.SendAsync("ReceiveDiscordState", GetStatePayload());
            }
            catch { }
        }

        public void Disconnect()
        {
            try { _readCts?.Cancel(); } catch { }
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
            Connected = false;
            VoiceChannelId = null;
            VoiceChannelName = null;
            _participants = new();
            foreach (var kv in _pending) kv.Value.TrySetCanceled();
            _pending.Clear();
        }
    }

    public class DiscordGrantRevokedException : Exception
    {
        public DiscordGrantRevokedException(string message) : base(message) { }
    }
}
