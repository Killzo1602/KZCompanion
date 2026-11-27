using System.Threading.Tasks;
using DroidDeck.Services;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Actions.Providers
{
    public class SoundboardProvider : IActionProvider
    {
        private readonly SoundboardService _soundboard;
        private readonly DiscordRpcService _discord;

        public SoundboardProvider(SoundboardService soundboard, DiscordRpcService discord)
        {
            _soundboard = soundboard;
            _discord = discord;
        }

        public string Type => "soundboard";

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Soundboard",
            Icon = "campaign",
            Category = "Streaming",
            Editor = "soundboard",
        };

        public async Task ExecuteAsync(ActionContext c)
        {
            if (c.Operation("play") == "stop")
            {
                _soundboard.StopAll();
                return;
            }

            if (c.Get("source", "myinstants").ToLowerInvariant() == "discord")
            {
                var guildId = c.Get("guildId");
                await _discord.PlaySoundboardSoundAsync(c.Get("soundId"), guildId.Length > 0 ? guildId : null);
                return;
            }

            var title = c.Get("title");
            await _soundboard.PlayAsync(c.Get("id"), c.Get("url"), title.Length > 0 ? title : null);
        }
    }
}
