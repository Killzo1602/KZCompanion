using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
    public class SpotifyProvider : IStatefulActionProvider
    {
        public const string TypeName = "spotify";

        private readonly SpotifyIntegration _spotify;

        public SpotifyProvider(SpotifyIntegration spotify) => _spotify = spotify;

        public string Type => TypeName;
        public string StateEvent => SpotifyIntegration.Event;
        public object GetState() => _spotify.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "Spotify",
            Icon = "music_note",
            Category = "Media",
            Description = "Controls Spotify on your (Premium) account, with cover art, like and shuffle on the button.",
            Stateful = true,
            Settings = "spotify",
            StateEvent = StateEvent,
            Fields =
            {
                ActionField.Select("operation", "What to do",
                    ("playPause", "Play/pause"),
                    ("next", "Next"),
                    ("previous", "Previous"),
                    ("like", "Like/unlike the track"),
                    ("shuffle", "Shuffle on/off"),
                    ("repeat", "Repeat (off → playlist → track)"),
                    ("volume", "Volume (+/-)"),
                    ("playlist", "Play a playlist"),
                    ("nowPlaying", "Show what's playing")),
                ActionField.Number("step", "Volume step", 10, -100, 100, "%").When("operation", "volume"),
                ActionField.Dynamic("uri", "Playlist", "/api/integrations/spotify/options/playlists", allowCustom: true).When("operation", "playlist"),
                ActionField.Toggle("shuffleOn", "Start shuffled").When("operation", "playlist"),
            },
        };

        public async Task ExecuteAsync(ActionContext ctx)
        {
            var p = _spotify.Current;
            switch (ctx.Operation("playpause"))
            {
                case "playpause":
                case "nowplaying":
                    await _spotify.CallAsync(HttpMethod.Put, p?.Playing == true ? "/me/player/pause" : "/me/player/play");
                    break;
                case "next":
                    await _spotify.CallAsync(HttpMethod.Post, "/me/player/next");
                    break;
                case "previous":
                    await _spotify.CallAsync(HttpMethod.Post, "/me/player/previous");
                    break;
                case "like":
                    await _spotify.SetLikedAsync(!(p?.Liked ?? false));
                    return;
                case "shuffle":
                    await _spotify.CallAsync(HttpMethod.Put, "/me/player/shuffle?state=" + (!(p?.Shuffle ?? false)).ToString().ToLowerInvariant());
                    break;
                case "repeat":
                    var next = (p?.Repeat ?? "off") switch { "off" => "context", "context" => "track", _ => "off" };
                    await _spotify.CallAsync(HttpMethod.Put, "/me/player/repeat?state=" + next);
                    break;
                case "volume":
                    var volume = Math.Clamp((p?.Volume ?? 50) + ctx.Int("step", 10), 0, 100);
                    await _spotify.CallAsync(HttpMethod.Put, "/me/player/volume?volume_percent=" + volume);
                    break;
                case "playlist":
                    var uri = ctx.Get("uri").Trim();
                    if (uri.Length == 0) throw new InvalidOperationException("Choose the playlist.");
                    if (ctx.Get("shuffleOn") == "true")
                        await _spotify.CallAsync(HttpMethod.Put, "/me/player/shuffle?state=true");
                    await _spotify.CallAsync(HttpMethod.Put, "/me/player/play", new { context_uri = uri });
                    break;
            }
            await Task.Delay(300);
            try { await _spotify.RefreshAsync(); } catch { }
        }
    }
}
