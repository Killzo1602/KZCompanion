using System.Threading.Tasks;
using DroidDeck.Actions;

namespace DroidDeck.Integrations
{
    public class StreamerBotProvider : IStatefulActionProvider
    {
        public const string TypeName = "streamerbot";

        private readonly StreamerBotIntegration _bot;

        public StreamerBotProvider(StreamerBotIntegration bot) => _bot = bot;

        public string Type => TypeName;
        public string StateEvent => StreamerBotIntegration.Event;
        public object GetState() => _bot.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "Streamer.bot",
            Icon = "smart_toy",
            Category = "Streaming",
            Description = "Runs a Streamer.bot action (with arguments) through its WebSocket Server.",
            Settings = "streamerbot",
            StateEvent = StateEvent,
            Fields =
            {
                ActionField.Dynamic("action", "Action", "/api/integrations/streamerbot/options/actions",
                    "Also accepts a typed action name.", allowCustom: true),
                ActionField.Multiline("args", "Arguments (optional)", "key=value",
                    "One per line (key=value) or a JSON object. They become %key% in Streamer.bot."),
            },
        };

        public Task ExecuteAsync(ActionContext ctx) => _bot.DoActionAsync(ctx.Get("action"), ctx.Get("args"));
    }
}
