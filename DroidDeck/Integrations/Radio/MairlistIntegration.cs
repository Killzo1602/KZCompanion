using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Actions;
using DroidDeck.Hubs;
using DroidDeck.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Integrations
{
    public class MairlistIntegration : PolledHttpIntegration
    {
        public const string Event = "ReceiveMairlistState";
        public const string DefaultUrl = "http://127.0.0.1:9300";

        private volatile string? _onAir;

        public MairlistIntegration(ILogger<MairlistIntegration> logger, IHubContext<DeckHub> hub,
            StreamDeckConfigService config, IHttpClientFactory http)
            : base(logger, hub, config, http) { }

        public override string Name => "mairlist";
        public override string Title => "mAirList";
        public override string StateEvent => Event;

        private string? Url => Get("url");

        protected override bool HasConfig => Url != null;

        protected override string UnreachableMessage =>
            "Couldn't reach mAirList. Check that it's open and the REST interface is on.";

        public bool? OnAir => Connected && _onAir != null ? _onAir == "1" : null;

        public override IntegrationStatus GetStatus() => new()
        {
            Connected = Connected,
            Configured = HasConfig,
            Account = Connected ? Url : null,
            Message = Error,
            Fields = new()
            {
                ["url"] = Url ?? DefaultUrl,
                ["username"] = Get("username") ?? "",
            },
        };

        public override object GetState() => new { connected = Connected, onAir = OnAir };

        public override Task<IntegrationStatus> ConfigureAsync(Dictionary<string, string> fields)
        {
            var url = NormalizeUrl(fields.GetValueOrDefault("url"), DefaultUrl);
            var password = fields.GetValueOrDefault("password") ?? "";
            Set(("url", url),
                ("username", fields.GetValueOrDefault("username")?.Trim()),
                ("password", password.Length > 0 ? password : Get("password")));
            return RestartAndWaitAsync();
        }

        public override Task LogoutAsync()
        {
            Clear();
            PollNow();
            return Task.CompletedTask;
        }

        protected override void ResetState() => _onAir = null;

        protected override async Task PollAsync(CancellationToken ct)
        {
            using var request = NewRequest(HttpMethod.Get, "/status/onair");
            using var response = await NewClient().SendAsync(request, ct);
            CheckAuth(response);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _onAir = null;
                return;
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
            _onAir = (await response.Content.ReadAsStringAsync(ct)).Trim();
        }

        public async Task ExecuteCommandAsync(string command)
        {
            if (!HasConfig) throw new InvalidOperationException("Configure o mAirList primeiro.");
            using var request = NewRequest(HttpMethod.Post, "/execute");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["command"] = command });
            using var response = await NewClient().SendAsync(request);
            CheckAuth(response);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException("Turn on the ExecuteCommand plugin in the mAirList REST interface.");
            response.EnsureSuccessStatusCode();
            PollNow();
        }

        private static void CheckAuth(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("mAirList refused the username or password.");
        }

        private HttpRequestMessage NewRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, Url + path);
            if (Get("username") is { } user)
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{Get("password")}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
            return request;
        }
    }

    public class MairlistProvider : IStatefulActionProvider
    {
        public const string TypeName = "mairlist";

        private static readonly string[] PlaylistOps =
            { "automation_toggle", "automation_on", "automation_off", "next", "player_toggle", "player_start", "player_stop", "player_fadeout" };

        private static readonly string[] PlayerOps = { "player_toggle", "player_start", "player_stop", "player_fadeout" };

        private readonly MairlistIntegration _mairlist;

        public MairlistProvider(MairlistIntegration mairlist) => _mairlist = mairlist;

        public string Type => TypeName;
        public string StateEvent => MairlistIntegration.Event;
        public object GetState() => _mairlist.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "mAirList",
            Icon = "radio",
            Category = "Studio",
            Stateful = true,
            Settings = "mairlist",
            StateEvent = StateEvent,
            Description = "On air, automation, players and cartwall in mAirList, through its REST interface.",
            Fields =
            {
                ActionField.Select("operation", "What to do",
                    ("on_air_toggle", "On air (toggle)"),
                    ("on_air", "Go on air"),
                    ("off_air", "Go off air"),
                    ("automation_toggle", "Automation (toggle)"),
                    ("automation_on", "Turn automation on"),
                    ("automation_off", "Turn automation off"),
                    ("next", "Next in playlist"),
                    ("player_toggle", "Player: play or stop"),
                    ("player_start", "Player: play"),
                    ("player_stop", "Player: stop"),
                    ("player_fadeout", "Player: fade out"),
                    ("cart", "Play a cart"),
                    ("cart_stop_all", "Stop all carts"),
                    ("custom", "Other command")),
                ActionField.Number("playlist", "Playlist", 1, 1, 16).When("operation", PlaylistOps),
                ActionField.Number("player", "Player", 1, 1, 16).When("operation", PlayerOps),
                ActionField.Number("cart", "Cart number", 1, 1, 999).When("operation", "cart"),
                ActionField.Text("command", "Command", "PLAYLIST 1 NEXT",
                    "A command from the mAirList remote command list.", required: true)
                    .When("operation", "custom"),
            },
        };

        public Task ExecuteAsync(ActionContext ctx) => _mairlist.ExecuteCommandAsync(BuildCommand(ctx));

        public static string BuildCommand(ActionContext c)
        {
            var playlist = Math.Clamp(c.Int("playlist", 1), 1, 16);
            var player = Math.Clamp(c.Int("player", 1), 1, 16);
            return c.Operation("on_air_toggle") switch
            {
                "on_air_toggle" => "ON/OFF AIR",
                "on_air" => "ON AIR",
                "off_air" => "OFF AIR",
                "automation_toggle" => $"AUTOMATION {playlist} ON/OFF",
                "automation_on" => $"AUTOMATION {playlist} ON",
                "automation_off" => $"AUTOMATION {playlist} OFF",
                "next" => $"PLAYLIST {playlist} NEXT",
                "player_toggle" => $"PLAYER {playlist}-{player} START/STOP",
                "player_start" => $"PLAYER {playlist}-{player} START",
                "player_stop" => $"PLAYER {playlist}-{player} STOP",
                "player_fadeout" => $"PLAYER {playlist}-{player} FADEOUT",
                "cart" => $"CARTWALL {Math.Clamp(c.Int("cart", 1), 1, 999)} CLICK",
                "cart_stop_all" => "CARTWALL ALL STOP",
                "custom" => CheckCommand(c.Get("command")),
                _ => throw new InvalidOperationException("Unknown operation."),
            };
        }

        public static string CheckCommand(string command)
        {
            var text = command.Trim();
            if (text.Length == 0) throw new InvalidOperationException("Preencha o comando.");
            foreach (var ch in text)
                if (char.IsControl(ch)) throw new InvalidOperationException("Comando inválido.");
            return text;
        }
    }
}
