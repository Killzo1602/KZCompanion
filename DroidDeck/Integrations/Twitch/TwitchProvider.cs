using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Actions;
using DroidDeck.Hubs;
using DroidDeck.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Integrations
{
    public class TwitchProvider : IStatefulActionProvider
    {
        public const string TypeName = "twitch";

        private readonly TwitchIntegration _twitch;

        public TwitchProvider(TwitchIntegration twitch) => _twitch = twitch;

        public string Type => TypeName;
        public string StateEvent => TwitchIntegration.Event;
        public object GetState() => _twitch.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "Twitch",
            Icon = "sensors",
            Category = "Streaming",
            Description = "Marker, clip, ad, chat, poll and title for your channel; shows whether you're live.",
            Stateful = true,
            Settings = "twitch",
            StateEvent = StateEvent,
            Fields =
            {
                ActionField.Select("operation", "What to do",
                    ("marker", "Create marker"),
                    ("clip", "Create clip"),
                    ("ad", "Run ad"),
                    ("chat", "Send chat message"),
                    ("poll", "Start poll"),
                    ("title", "Change the title"),
                    ("status", "Only show live/viewers")),
                ActionField.Text("description", "Description (optional)", "Epic moment").When("operation", "marker"),
                ActionField.Select("length", "Duration", ("30", "30 s"), ("60", "1 min"), ("90", "1 min 30"), ("120", "2 min"), ("180", "3 min")).When("operation", "ad"),
                ActionField.Multiline("message", "Message", "Follow the channel!").When("operation", "chat"),
                ActionField.Text("question", "Question", "Which game next?", required: true).When("operation", "poll"),
                ActionField.Multiline("choices", "Options (one per line, 2 to 5)", "Yes\nNo").When("operation", "poll"),
                ActionField.Number("duration", "Duration", 60, 15, 1800, "s").When("operation", "poll"),
                ActionField.Text("title", "New title", required: true).When("operation", "title"),
            },
        };

        public async Task ExecuteAsync(ActionContext ctx)
        {
            var id = _twitch.UserId ?? throw new InvalidOperationException("Twitch disconnected.");
            switch (ctx.Operation("marker"))
            {
                case "marker":
                    var description = ctx.Get("description").Trim();
                    var marker = new Dictionary<string, string> { ["user_id"] = id };
                    if (description.Length > 0) marker["description"] = description;
                    await _twitch.CallAsync(HttpMethod.Post, "/streams/markers", marker);
                    break;
                case "clip":
                    await _twitch.CallAsync(HttpMethod.Post, "/clips?broadcaster_id=" + id);
                    break;
                case "ad":
                    await _twitch.CallAsync(HttpMethod.Post, "/channels/commercial", new { broadcaster_id = id, length = ctx.Int("length", 30) });
                    break;
                case "chat":
                    var message = ctx.Get("message").Trim();
                    if (message.Length == 0) return;
                    await _twitch.CallAsync(HttpMethod.Post, "/chat/messages", new { broadcaster_id = id, sender_id = id, message });
                    break;
                case "poll":
                    var choices = ctx.Get("choices").Split('\n').Select(c => c.Trim()).Where(c => c.Length > 0).Take(5)
                        .Select(c => new { title = c }).ToArray();
                    if (choices.Length < 2) throw new InvalidOperationException("The poll needs 2 options.");
                    await _twitch.CallAsync(HttpMethod.Post, "/polls", new
                    {
                        broadcaster_id = id,
                        title = ctx.Get("question").Trim(),
                        choices,
                        duration = Math.Clamp(ctx.Int("duration", 60), 15, 1800),
                    });
                    break;
                case "title":
                    await _twitch.CallAsync(new HttpMethod("PATCH"), "/channels?broadcaster_id=" + id, new { title = ctx.Get("title").Trim() });
                    _twitch.RefreshSoon();
                    break;
                case "status":
                    _twitch.RefreshSoon();
                    break;
            }
        }
    }
}
