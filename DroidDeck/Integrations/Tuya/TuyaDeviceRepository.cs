using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Services.Tuya
{
    public class TuyaDeviceRepository
    {
        private readonly TuyaApiClient _api;
        private readonly ILogger _logger;

        public TuyaDeviceRepository(TuyaApiClient api, ILogger logger)
        {
            _api = api;
            _logger = logger;
        }

        public async Task<List<TuyaHome>> QueryHomesAsync()
        {
            var result = await _api.GetAsync("/v1.0/m/life/users/homes");
            var homes = new List<TuyaHome>();
            if (result.ValueKind != JsonValueKind.Array) return homes;

            foreach (var h in result.EnumerateArray())
            {
                homes.Add(new TuyaHome
                {
                    Id = Str(h, "ownerId"),
                    Name = Str(h, "name"),
                });
            }
            return homes;
        }

        public async Task<List<TuyaDevice>> QueryDevicesAsync(string homeId)
        {
            var result = await _api.GetAsync("/v1.0/m/life/ha/home/devices",
                new Dictionary<string, object?> { ["homeId"] = homeId });

            var devices = new List<TuyaDevice>();
            if (result.ValueKind != JsonValueKind.Array) return devices;

            foreach (var item in result.EnumerateArray())
            {
                var device = new TuyaDevice
                {
                    Id = Str(item, "id"),
                    Name = Str(item, "name"),
                    Category = Str(item, "category"),
                    ProductName = Str(item, "product_name"),
                    Icon = Str(item, "icon"),
                    Online = item.TryGetProperty("online", out var on) && on.ValueKind == JsonValueKind.True,
                };

                if (item.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in status.EnumerateArray())
                    {
                        if (s.TryGetProperty("code", out var c) && s.TryGetProperty("value", out var v))
                            device.Status[c.GetString() ?? ""] = ToClr(v);
                    }
                }

                try { await LoadSpecificationsAsync(device); }
                catch (Exception ex) { _logger.LogWarning("Tuya: specifications de {Id} falhou: {Msg}", device.Id, ex.Message); }

                try { await LoadStrategyAsync(device); }
                catch (Exception ex) { _logger.LogWarning("Tuya: strategy de {Id} falhou: {Msg}", device.Id, ex.Message); }

                devices.Add(device);
            }
            return devices;
        }

        public async Task<List<(string Id, bool Online, Dictionary<string, object?> Status)>> QueryStatusAsync(string homeId)
        {
            var result = await _api.GetAsync("/v1.0/m/life/ha/home/devices",
                new Dictionary<string, object?> { ["homeId"] = homeId });

            var list = new List<(string, bool, Dictionary<string, object?>)>();
            if (result.ValueKind != JsonValueKind.Array) return list;

            foreach (var item in result.EnumerateArray())
            {
                var status = new Dictionary<string, object?>();
                if (item.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in st.EnumerateArray())
                    {
                        if (s.TryGetProperty("code", out var c) && s.TryGetProperty("value", out var v))
                            status[c.GetString() ?? ""] = ToClr(v);
                    }
                }
                list.Add((Str(item, "id"),
                    item.TryGetProperty("online", out var on) && on.ValueKind == JsonValueKind.True,
                    status));
            }
            return list;
        }

        private async Task LoadSpecificationsAsync(TuyaDevice device)
        {
            var spec = await _api.GetAsync($"/v1.1/m/life/{device.Id}/specifications");
            if (spec.ValueKind != JsonValueKind.Object) return;

            if (spec.TryGetProperty("functions", out var functions) && functions.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in functions.EnumerateArray())
                {
                    var code = Str(f, "code");
                    if (string.IsNullOrEmpty(code)) continue;
                    device.Functions[code] = new TuyaFunction
                    {
                        Type = Str(f, "type"),
                        Values = Str(f, "values"),
                    };
                }
            }
        }

        private async Task LoadStrategyAsync(TuyaDevice device)
        {
            var result = await _api.GetAsync($"/v1.0/m/life/devices/{device.Id}/status");
            if (result.ValueKind != JsonValueKind.Object) return;

            var supportLocal = true;
            if (result.TryGetProperty("dpStatusRelationDTOS", out var rels) && rels.ValueKind == JsonValueKind.Array)
            {
                foreach (var rel in rels.EnumerateArray())
                {
                    if (!(rel.TryGetProperty("supportLocal", out var sl) && sl.ValueKind == JsonValueKind.True))
                    {
                        supportLocal = false;
                        break;
                    }

                    var dpId = rel.TryGetProperty("dpId", out var d) ? d.ToString() : null;
                    var statusCode = Str(rel, "statusCode");
                    if (!string.IsNullOrEmpty(dpId) && !string.IsNullOrEmpty(statusCode))
                        device.DpIdToCode[dpId] = statusCode;
                }
            }

            if (supportLocal)
            {
                try
                {
                    var custom = await _api.GetAsync($"/v1.0/m/life/ha/{device.Id}/code/custom-type");
                    if (custom.ValueKind == JsonValueKind.True) supportLocal = false;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Tuya: custom-type de {Id}: {Msg}", device.Id, ex.Message);
                }
            }

            device.SupportLocal = supportLocal;
            if (!supportLocal) device.DpIdToCode.Clear();
        }

        public Task SendCommandAsync(string deviceId, string code, object? value)
        {
            var command = new Dictionary<string, object?> { ["code"] = code, ["value"] = value };
            return SendCommandsAsync(deviceId, new List<Dictionary<string, object?>> { command });
        }

        public async Task SendCommandsAsync(string deviceId, IReadOnlyList<Dictionary<string, object?>> commands)
        {
            await _api.PostAsync($"/v1.1/m/thing/{deviceId}/commands", null,
                new Dictionary<string, object?> { ["commands"] = commands });
        }

        private static string Str(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return "";
            return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        }

        internal static object? ToClr(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetInt64(out var l) ? l : v.GetDouble(),
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => v.GetRawText(),
        };
    }
}
