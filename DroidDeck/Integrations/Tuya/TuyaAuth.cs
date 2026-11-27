using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Services.Tuya
{
    public class TuyaAuth
    {
        private const string AuthHost = "https://apigw.iotbing.com";

        private readonly HttpClient _http;
        private readonly ILogger _logger;

        public TuyaAuth(HttpClient http, ILogger logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<(string Token, string QrPayload)> RequestQrCodeAsync(
            string userCode, string clientId, string schema)
        {
            var url = $"{AuthHost}/v1.0/m/life/home-assistant/qrcode/tokens" +
                      $"?clientid={Uri.EscapeDataString(clientId)}" +
                      $"&usercode={Uri.EscapeDataString(userCode)}" +
                      $"&schema={Uri.EscapeDataString(schema)}";

            using var resp = await _http.PostAsync(url, null);
            var raw = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean())
            {
                var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : raw;
                throw new InvalidOperationException($"Tuya recusou o pedido de QR: {msg}");
            }

            var token = root.GetProperty("result").GetProperty("qrcode").GetString() ?? "";
            return (token, $"tuyaSmart--qrLogin?token={token}");
        }

        public async Task<TuyaToken?> TryCompleteLoginAsync(string qrToken, string userCode, string clientId)
        {
            var url = $"{AuthHost}/v1.0/m/life/home-assistant/qrcode/tokens/{Uri.EscapeDataString(qrToken)}" +
                      $"?clientid={Uri.EscapeDataString(clientId)}" +
                      $"&usercode={Uri.EscapeDataString(userCode)}";

            using var resp = await _http.GetAsync(url);
            var raw = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean())
                return null;

            var result = root.GetProperty("result");
            return new TuyaToken
            {
                AccessToken = result.GetProperty("access_token").GetString() ?? "",
                RefreshToken = result.GetProperty("refresh_token").GetString() ?? "",
                Uid = result.GetProperty("uid").GetString() ?? "",
                TerminalId = result.GetProperty("terminal_id").GetString() ?? "",
                Endpoint = result.GetProperty("endpoint").GetString() ?? "",
                ExpiresInSeconds = result.TryGetProperty("expire_time", out var e) ? e.GetInt64() : 0,
                IssuedAt = root.TryGetProperty("t", out var tt)
                    ? tt.GetInt64()
                    : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
        }
    }
}
