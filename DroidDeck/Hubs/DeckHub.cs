using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using DroidDeck.Lib;

namespace DroidDeck.Hubs
{
    public class DeckHub : Hub
    {
        private readonly IServiceProvider _services;

        private static int _connectedCount = 0;
        public static int ConnectedCount => Volatile.Read(ref _connectedCount);

        public DeckHub(IServiceProvider services)
        {
            _services = services;
        }

        public override async Task OnConnectedAsync()
        {
            Interlocked.Increment(ref _connectedCount);
            Context.Items["since"] = DateTime.UtcNow;
            Lib.Log.Info($"Hub: cliente conectado {Context.ConnectionId[..6]} ({Context.GetHttpContext()?.Connection.RemoteIpAddress}); total {ConnectedCount}");
            var http = Context.GetHttpContext();
            var language = http?.Request.Query["lang"].ToString() is { Length: > 0 } q
                ? L.LanguageOf(q)
                : L.LanguageOf(http?.Request.Headers.AcceptLanguage.ToString());
            Context.Items[LanguageKey] = language;
            L.Language = language;
            await Groups.AddToGroupAsync(Context.ConnectionId, LanguageGroup(language));
            if (_services.GetService(typeof(Services.StreamDeckConfigService)) is Services.StreamDeckConfigService cfg)
            {
                try
                {
                    var layout = cfg.GetLayout();
                    await Clients.Caller.SendAsync("ReceiveLayoutUpdate",
                        new { rows = layout.Rows, columns = layout.Columns });
                }
                catch { }
            }
            if (_services.GetService(typeof(Services.ActionExecutorService)) is Services.ActionExecutorService executor)
            {
                foreach (var provider in executor.StatefulProviders)
                {
                    try { await Clients.Caller.SendAsync(provider.StateEvent, provider.GetState()); }
                    catch { }
                }
            }
            if (_services.GetService(typeof(Services.ButtonToggleService)) is Services.ButtonToggleService toggles)
            {
                try { await Clients.Caller.SendAsync(Services.ButtonToggleService.Event, toggles.GetState()); }
                catch { }
            }
            if (_services.GetService(typeof(Services.InfoService)) is Services.InfoService info)
            {
                try
                {
                    await Clients.Caller.SendAsync(Services.InfoService.Event, info.GetState());
                    if (info.NowPlaying is { } np)
                        await Clients.Caller.SendAsync(Services.InfoService.NowPlayingEvent, np);
                }
                catch { }
            }
            if (_services.GetService(typeof(Services.ForegroundWatcher)) is Services.ForegroundWatcher fg)
            {
                try { await Clients.Caller.SendAsync(Services.ForegroundWatcher.Event, fg.GetState()); }
                catch { }
            }
            await base.OnConnectedAsync();
        }

        public void WatchRunningApps() =>
            (_services.GetService(typeof(Services.RunningAppsService)) as Services.RunningAppsService)?.Watch(Context.ConnectionId);

        public void UnwatchRunningApps() =>
            (_services.GetService(typeof(Services.RunningAppsService)) as Services.RunningAppsService)?.Unwatch(Context.ConnectionId);

        public async Task SetLanguage(string language)
        {
            language = L.LanguageOf(language);
            if (Context.Items.TryGetValue(LanguageKey, out var old) && old is string o)
            {
                if (o == language) return;
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, LanguageGroup(o));
            }
            Context.Items[LanguageKey] = language;
            L.Language = language;
            await Groups.AddToGroupAsync(Context.ConnectionId, LanguageGroup(language));
            if (_services.GetService(typeof(Services.InfoService)) is Services.InfoService info)
            {
                try { await Clients.Caller.SendAsync(Services.InfoService.Event, info.GetState()); }
                catch { }
            }
        }

        private const string LanguageKey = "lang";

        public static IReadOnlyList<string> Languages => L.Languages;

        public static string LanguageGroup(string language) => "lang:" + language;

        public void HoldKey(string buttonId)
        {
            if (FindHoldKey(buttonId) is ushort vk)
                Lib.Input.HeldKeys.Shared.Down(Context.ConnectionId, vk);
            else if (FindHoldMute(buttonId) is { } parameter)
                MicHold?.Down(Context.ConnectionId, parameter);
        }

        public void ReleaseKey(string buttonId)
        {
            if (FindHoldKey(buttonId) is ushort vk)
                Lib.Input.HeldKeys.Shared.Up(Context.ConnectionId, vk);
            else if (FindHoldMute(buttonId) is { } parameter)
                MicHold?.Up(Context.ConnectionId, parameter);
        }

        private Integrations.MicHoldService? MicHold =>
            _services.GetService(typeof(Integrations.MicHoldService)) as Integrations.MicHoldService;

        private Integrations.CompanionIntegration? Companion =>
            _services.GetService(typeof(Integrations.CompanionIntegration)) as Integrations.CompanionIntegration;

        public Task CompanionKey(int key, bool pressed) =>
            Companion?.SetKeyAsync(Context.ConnectionId, key, pressed) ?? Task.CompletedTask;

        private string? FindHoldMute(string buttonId)
        {
            if (string.IsNullOrEmpty(buttonId)) return null;
            if (_services.GetService(typeof(Services.StreamDeckConfigService)) is not Services.StreamDeckConfigService cfg)
                return null;
            var action = cfg.GetProfiles()
                .SelectMany(p => p.Buttons ?? new List<Models.DeckButton>())
                .FirstOrDefault(b => b.Id == buttonId)?.Action;
            return action == null ? null : Integrations.VoicemeeterProvider.HoldMuteParameter(action);
        }

        private ushort? FindHoldKey(string buttonId)
        {
            if (string.IsNullOrEmpty(buttonId)) return null;
            if (_services.GetService(typeof(Services.StreamDeckConfigService)) is not Services.StreamDeckConfigService cfg)
                return null;
            var action = cfg.GetProfiles()
                .SelectMany(p => p.Buttons ?? new List<Models.DeckButton>())
                .FirstOrDefault(b => b.Id == buttonId)?.Action;
            if (action == null || !IsKeyType(action.Type)) return null;
            if (action.Parameters.TryGetValue("mode", out var mode) && mode is { Length: > 0 } &&
                !string.Equals(mode, "hold", StringComparison.OrdinalIgnoreCase))
                return null;
            return action.Parameters.TryGetValue("key", out var key) &&
                   Lib.Input.KeyboardInput.TryResolveKey(key, out var vk) ? vk : null;
        }

        private static bool IsKeyType(string? type) =>
            type != null && (type.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                             type.Equals("keyboard_key", StringComparison.OrdinalIgnoreCase) ||
                             type.Equals("send_key", StringComparison.OrdinalIgnoreCase));

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Lib.Input.HeldKeys.Shared.ReleaseOwner(Context.ConnectionId);
            (_services.GetService(typeof(Services.RunningAppsService)) as Services.RunningAppsService)?.Unwatch(Context.ConnectionId);
            MicHold?.ReleaseOwner(Context.ConnectionId);
            Companion?.ReleaseOwner(Context.ConnectionId);
            Interlocked.Decrement(ref _connectedCount);
            var secs = Context.Items.TryGetValue("since", out var t) && t is DateTime d ? (int)(DateTime.UtcNow - d).TotalSeconds : -1;
            Lib.Log.Info($"Hub: cliente saiu {Context.ConnectionId[..6]} apos {secs}s; motivo: {(exception == null ? "fechou normalmente / timeout do cliente" : exception.GetType().Name + ": " + exception.Message)}; total {ConnectedCount}");
            await base.OnDisconnectedAsync(exception);
        }
    }

    public static class LocalizedHubExtensions
    {
        public static async Task SendLocalizedAsync(this IHubContext<DeckHub> hub, string method, Func<object?> payload)
        {
            var previous = L.Language;
            try
            {
                foreach (var language in DeckHub.Languages)
                {
                    L.Language = language;
                    await hub.Clients.Group(DeckHub.LanguageGroup(language)).SendAsync(method, payload());
                }
            }
            finally
            {
                L.Language = previous;
            }
        }
    }
}
