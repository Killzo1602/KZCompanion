using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Actions;
using DroidDeck.Hubs;
using DroidDeck.Lib;
using DroidDeck.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Integrations
{
    public class HueIntegration : AccountIntegration
    {
        public const string Event = "ReceiveHueState";
        private const string DiscoveryUrl = "https://discovery.meethue.com/";

        private readonly IHttpClientFactory _http;
        private bool _connected;
        private string? _error;
        private string? _bridgeName;
        private CancellationTokenSource? _recheck;

        public HueIntegration(ILogger<HueIntegration> logger, IHubContext<DeckHub> hub, StreamDeckConfigService config,
            IHttpClientFactory http) : base(logger, hub, config)
        {
            _http = http;
        }

        public override string Name => "hue";
        public override string Title => "Philips Hue";
        public override string StateEvent => Event;

        private string? Bridge => Get("bridge");
        private string? Username => Get("username");

        public override IntegrationStatus GetStatus() => new()
        {
            Connected = _connected,
            Configured = Bridge != null && Username != null,
            Account = _connected ? (_bridgeName ?? Bridge) : null,
            Message = _connected ? null : _error,
            Fields = new() { ["bridge"] = Bridge ?? "" },
        };

        public override object GetState() => new { connected = _connected };

        public override async Task<IntegrationStatus> ConfigureAsync(Dictionary<string, string> fields)
        {
            var bridge = NormalizeHost(fields.GetValueOrDefault("bridge"));
            if (bridge.Length == 0)
            {
                bridge = await DiscoverAsync()
                    ?? throw new InvalidOperationException("No Hue Bridge found. Type its IP (shown in the Hue app, under Settings → Bridges).");
            }

            if (bridge != Bridge || Username == null || !await CheckAsync())
            {
                var username = await PairAsync(bridge);
                Set(("bridge", bridge), ("username", username));
            }
            await CheckAsync();
            _recheck?.Cancel();
            return GetStatus();
        }

        public override Task LogoutAsync()
        {
            Clear();
            _connected = false;
            _error = null;
            _bridgeName = null;
            _ = BroadcastAsync();
            return Task.CompletedTask;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _recheck = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                if (Bridge != null && Username != null) await CheckAsync();
                await Wait(TimeSpan.FromMinutes(_connected ? 5 : 1), _recheck.Token);
            }
        }

        private async Task<bool> CheckAsync()
        {
            var was = _connected;
            try
            {
                var config = await SendAsync(HttpMethod.Get, "config");
                _bridgeName = config?["name"]?.GetValue<string>();
                _connected = _bridgeName != null || config?["bridgeid"] != null;
                _error = _connected ? null : "The Hue Bridge refused access. Pair again.";
            }
            catch (Exception ex)
            {
                _connected = false;
                _error = ex is HttpRequestException or TaskCanceledException
                    ? "Couldn't reach the Hue Bridge. Check the IP."
                    : ex.Message;
            }
            if (was != _connected) await BroadcastAsync();
            return _connected;
        }

        private async Task<string?> DiscoverAsync()
        {
            try
            {
                var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                var list = JsonNode.Parse(await client.GetStringAsync(DiscoveryUrl)) as JsonArray;
                return list?.Select(b => b?["internalipaddress"]?.GetValue<string>()).FirstOrDefault(ip => !string.IsNullOrEmpty(ip));
            }
            catch (Exception ex)
            {
                Logger.LogInformation("Hue: descoberta falhou ({Error})", ex.Message);
                return null;
            }
        }

        private async Task<string> PairAsync(string bridge)
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            var body = new JsonObject { ["devicetype"] = "droiddeck#" + DeviceName() };
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try { response = await client.PostAsync($"http://{bridge}/api", content); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new InvalidOperationException("Couldn't reach the Hue Bridge. Check the IP.");
            }
            using (response)
                return ParsePairing(await response.Content.ReadAsStringAsync());
        }

        private static string DeviceName()
        {
            var name = new string(Environment.MachineName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return name.Length == 0 ? "pc" : name[..Math.Min(name.Length, 19)];
        }

        public static string ParsePairing(string body)
        {
            var first = (JsonNode.Parse(body) as JsonArray)?.FirstOrDefault();
            if (first?["success"]?["username"]?.GetValue<string>() is { Length: > 0 } user) return user;
            var type = first?["error"]?["type"]?.GetValue<int>();
            if (type == 101) throw new InvalidOperationException("Press the round button on the Hue Bridge and tap Pair again (within 30 seconds).");
            throw new InvalidOperationException(first?["error"]?["description"]?.GetValue<string>() ?? "The Hue Bridge refused the pairing.");
        }

        public static string NormalizeHost(string? value)
        {
            var h = (value ?? "").Trim();
            foreach (var prefix in new[] { "http://", "https://" })
                if (h.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) h = h[prefix.Length..];
            return h.Trim('/');
        }

        public async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body = null)
        {
            var bridge = Bridge;
            var user = Username;
            if (bridge == null || user == null) throw new InvalidOperationException("Philips Hue is not paired.");
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            using var request = new HttpRequestMessage(method, $"http://{bridge}/api/{user}/{path}");
            if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var node = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            ThrowIfError(node);
            return node;
        }

        public static void ThrowIfError(JsonNode? node)
        {
            if (node is not JsonArray list) return;
            var error = list.Select(i => i?["error"]).FirstOrDefault(e => e != null);
            if (error == null) return;
            if (error["type"]?.GetValue<int>() == 1) throw new InvalidOperationException("The Hue Bridge refused access. Pair again.");
            throw new InvalidOperationException(error["description"]?.GetValue<string>() ?? "The Hue Bridge refused.");
        }

        public async Task<JsonNode?> CallAsync(HttpMethod method, string path, JsonObject? body = null)
        {
            try
            {
                var result = await SendAsync(method, path, body);
                if (!_connected) { _connected = true; _error = null; await BroadcastAsync(); }
                return result;
            }
            catch (HttpRequestException)
            {
                if (_connected) { _connected = false; await BroadcastAsync(); }
                _error = "Couldn't reach the Hue Bridge. Check the IP.";
                throw new InvalidOperationException(_error);
            }
        }

        public override async Task<List<FieldOption>> OptionsAsync(string list, IQueryCollection query)
        {
            if (list == "items") list = query["target"] == "group" ? "groups" : "lights";
            if (list is not ("lights" or "groups" or "scenes")) return await base.OptionsAsync(list, query);
            var node = await CallAsync(HttpMethod.Get, list) as JsonObject;
            var groups = list == "scenes" ? await CallAsync(HttpMethod.Get, "groups") as JsonObject : null;
            return ParseOptions(list, node, groups);
        }

        public static List<FieldOption> ParseOptions(string list, JsonObject? node, JsonObject? groups = null)
        {
            var options = new List<FieldOption>();
            if (list == "groups") options.Add(new FieldOption("0", L.T("All lights")));
            if (node == null) return options;
            foreach (var (id, item) in node)
            {
                var name = item?["name"]?.GetValue<string>() ?? id;
                if (list == "groups" && item?["type"]?.GetValue<string>() is "Entertainment" or "LightSource" or "Luminaire") continue;
                if (list == "scenes")
                {
                    if (item?["recycle"]?.GetValue<bool>() == true) continue;
                    if (item?["group"]?.GetValue<string>() is { } group &&
                        groups?[group]?["name"]?.GetValue<string>() is { } room)
                        name = $"{name} ({room})";
                }
                options.Add(new FieldOption(id, name));
            }
            return options.OrderBy(o => o.Value == "0" ? "" : o.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
