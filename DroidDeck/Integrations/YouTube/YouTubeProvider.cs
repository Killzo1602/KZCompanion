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
    public class YouTubeProvider : IStatefulActionProvider
    {
        public const string TypeName = "youtube";

        private readonly YouTubeIntegration _youtube;

        public YouTubeProvider(YouTubeIntegration youtube) => _youtube = youtube;

        public string Type => TypeName;
        public string StateEvent => YouTubeIntegration.Event;
        public object GetState() => _youtube.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "YouTube",
            Icon = "smart_display",
            Category = "Streaming",
            Description = "Sends messages to your live chat and shows whether you're live and how many are watching.",
            Stateful = true,
            Settings = "youtube",
            StateEvent = StateEvent,
            Fields =
            {
                ActionField.Select("operation", "What to do",
                    ("chat", "Send chat message"),
                    ("status", "Only show live/viewers")),
                ActionField.Multiline("message", "Message", "Thanks for watching!").When("operation", "chat"),
            },
        };

        public async Task ExecuteAsync(ActionContext ctx)
        {
            switch (ctx.Operation("chat"))
            {
                case "chat":
                    var message = ctx.Get("message").Trim();
                    if (message.Length > 0) await _youtube.SendChatAsync(message);
                    break;
                default:
                    _youtube.RefreshSoon();
                    break;
            }
        }
    }
}
