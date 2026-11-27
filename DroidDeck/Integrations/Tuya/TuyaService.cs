using DroidDeck.Lib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Hubs;
using DroidDeck.Services.Tuya;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Services
{
    public class TuyaService : IAsyncDisposable, Integrations.IIntegration
    {
        private readonly ILogger<TuyaService> _logger;
        private readonly IHubContext<DeckHub> _hub;
        private readonly HttpClient _http;

        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly ConcurrentDictionary<string, TuyaDevice> _devices = new();

        private TuyaConfig _config = new();
        private List<TuyaHome> _homes = new();
        private TuyaApiClient? _api;
        private TuyaDeviceRepository? _repo;
        private TuyaMqttClient? _mqtt;

        private (string Token, string UserCode, DateTime Expires)? _pendingPairing;

        public bool Connected => _api != null;

        public string Name => "tuya";
        public string Title => "Tuya";
        public bool IsConnected => Connected;
        public bool PushConnected => _mqtt?.Connected ?? false;

        public TuyaService(ILogger<TuyaService> logger, IHubContext<DeckHub> hub, IHttpClientFactory httpFactory)
        {
            _logger = logger;
            _hub = hub;
            _http = httpFactory.CreateClient("tuya");
            _config = LoadConfig();
        }

        private static string ConfigPath
        {
            get
            {
                var dir = AppPaths.LocalDir;
                return Path.Combine(dir, "tuya.json");
            }
        }

        public TuyaConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<TuyaConfig>(SecretFile.ReadAllText(ConfigPath)) ?? new TuyaConfig();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Tuya: config ilegivel ({Msg}); comecando do zero", ex.Message);
            }
            return new TuyaConfig();
        }

        public bool HasSavedSession() => _config.HasSession;

        private void SaveConfig()
        {
            var path = ConfigPath;
            var tmp = path + ".tmp";
            try
            {
                SecretFile.WriteAllText(tmp, JsonSerializer.Serialize(_config));

                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                _logger.LogError("Tuya: falha ao salvar config: {Msg}", ex.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        public async Task<object> StartPairingAsync(string userCode)
        {
            var auth = new TuyaAuth(_http, _logger);
            var (token, payload) = await auth.RequestQrCodeAsync(userCode, _config.ClientId, _config.Schema);

            _pendingPairing = (token, userCode, DateTime.UtcNow.AddMinutes(2));

            var png = Convert.ToBase64String(Lib.PairingInfo.BuildQrPng(payload));

            return new { qrPayload = payload, qrPng = png, expiresInSeconds = 120 };
        }

        public async Task<bool> PollPairingAsync()
        {
            if (_pendingPairing is not { } pending) return false;

            if (DateTime.UtcNow > pending.Expires)
            {
                _pendingPairing = null;
                throw new InvalidOperationException("QR code expired; generate a new one.");
            }

            var auth = new TuyaAuth(_http, _logger);
            var token = await auth.TryCompleteLoginAsync(pending.Token, pending.UserCode, _config.ClientId);
            if (token == null) return false;

            _pendingPairing = null;
            _config.UserCode = pending.UserCode;
            _config.Token = token;
            SaveConfig();

            _logger.LogInformation("Tuya: pareado (uid {Uid}, endpoint {Endpoint})", token.Uid, token.Endpoint);

            await ConnectAsync();
            return true;
        }

        public async Task ConnectAsync()
        {
            await _connectLock.WaitAsync();
            try
            {
                if (!_config.HasSession)
                    throw new InvalidOperationException("Tuya is not paired.");

                var api = new TuyaApiClient(_http, _logger, _config.Token!, _config.ClientId);
                api.TokenRefreshed += token =>
                {
                    _config.Token = token;
                    SaveConfig();
                };

                _api = api;
                _repo = new TuyaDeviceRepository(api, _logger);

                try
                {
                    await RefreshDevicesInternalAsync();
                    await StartPushAsync();
                }
                catch
                {
                    _api = null;
                    _repo = null;
                    throw;
                }

                await BroadcastStateAsync();
            }
            finally
            {
                _connectLock.Release();
            }
        }

        public async Task RefreshDevicesAsync()
        {
            if (_repo == null) throw new InvalidOperationException("Tuya is not connected.");
            await RefreshDevicesInternalAsync();
            await StartPushAsync();
            await BroadcastStateAsync();
        }

        private async Task RefreshDevicesInternalAsync()
        {
            _homes = await _repo!.QueryHomesAsync();

            _devices.Clear();
            foreach (var home in _homes)
            {
                foreach (var device in await _repo.QueryDevicesAsync(home.Id))
                    _devices[device.Id] = device;
            }

            _logger.LogInformation("Tuya: {N} dispositivo(s) em {H} casa(s)", _devices.Count, _homes.Count);
        }

        private async Task StartPushAsync()
        {
            if (_api == null) return;

            if (_mqtt != null)
            {
                await _mqtt.DisposeAsync();
                _mqtt = null;
            }

            var mqtt = new TuyaMqttClient(_api, _logger);
            mqtt.UpdateDevices(_homes, _devices.Values.ToList());
            mqtt.StatusReported += OnStatusReported;
            var first = true;
            mqtt.ConnectionChanged += connected =>
            {
                if (connected && !first) _ = ResyncStatusAsync();
                if (connected) first = false;
                _ = BroadcastStateAsync();
            };
            mqtt.Start();
            _mqtt = mqtt;
        }

        private void OnStatusReported(TuyaStatusReport report) => ApplyStatus(report.DeviceId, report.Changes);

        private async Task ResyncStatusAsync()
        {
            var repo = _repo;
            if (repo == null) return;
            try
            {
                foreach (var home in _homes)
                {
                    foreach (var (deviceId, online, status) in await repo.QueryStatusAsync(home.Id))
                    {
                        if (_devices.TryGetValue(deviceId, out var device)) device.Online = online;
                        ApplyStatus(deviceId, status);
                    }
                }
                await BroadcastStateAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Tuya: ressincronizar estado falhou: {Msg}", ex.Message);
            }
        }

        private void ApplyStatus(string deviceId, Dictionary<string, object?> changes)
        {
            if (!_devices.TryGetValue(deviceId, out var device)) return;

            lock (device.Status)
            {
                foreach (var change in changes)
                    device.Status[change.Key] = change.Value;
            }

            _ = _hub.Clients.All.SendAsync("ReceiveTuyaDeviceState", new
            {
                deviceId,
                status = changes,
            });
        }

        public IReadOnlyCollection<TuyaDevice> Devices => _devices.Values.ToList();

        public TuyaDevice? GetDevice(string deviceId)
            => _devices.TryGetValue(deviceId, out var d) ? d : null;

        public async Task SendCommandAsync(string deviceId, string code, object? value)
        {
            if (_repo == null) throw new InvalidOperationException("Tuya is not connected.");

            var device = GetDevice(deviceId);
            object? previous = null;
            var hadPrevious = false;
            if (device != null)
                lock (device.Status) hadPrevious = device.Status.TryGetValue(code, out previous);

            ApplyStatus(deviceId, new Dictionary<string, object?> { [code] = value });
            try
            {
                await _repo.SendCommandAsync(deviceId, code, value);
            }
            catch
            {
                if (device != null)
                {
                    lock (device.Status)
                    {
                        if (hadPrevious) device.Status[code] = previous;
                        else device.Status.Remove(code);
                    }
                }
                await BroadcastStateAsync();
                throw;
            }
        }

        public async Task ToggleAsync(string deviceId, string code)
        {
            var device = GetDevice(deviceId)
                ?? throw new InvalidOperationException($"Dispositivo {deviceId} desconhecido.");

            await SendCommandAsync(deviceId, code, ToggleTarget(device, code));
        }

        public static bool ToggleTarget(TuyaDevice device, string code)
        {
            lock (device.Status)
                return !(device.Status.TryGetValue(code, out var v) && v is bool b && b);
        }

        public object GetStatePayload() => new
        {
            connected = Connected,
            push = PushConnected,
            paired = _config.HasSession,
            userCode = _config.UserCode,
            devices = _devices.Values.Select(d => new
            {
                id = d.Id,
                name = d.Name,
                category = d.Category,
                productName = d.ProductName,
                online = d.Online,
                icon = d.Icon,
                status = SnapshotStatus(d),
                functions = d.Functions.ToDictionary(f => f.Key, f => new { type = f.Value.Type, values = f.Value.Values }),
            }),
        };

        private static Dictionary<string, object?> SnapshotStatus(TuyaDevice d)
        {
            lock (d.Status) return new Dictionary<string, object?>(d.Status);
        }

        private async Task BroadcastStateAsync()
        {
            try { await _hub.Clients.All.SendAsync("ReceiveTuyaState", GetStatePayload()); }
            catch (Exception ex) { _logger.LogDebug("Tuya: broadcast falhou: {Msg}", ex.Message); }
        }

        public async ValueTask DisposeAsync()
        {
            if (_mqtt != null) await _mqtt.DisposeAsync();
            _connectLock.Dispose();
        }
    }
}
