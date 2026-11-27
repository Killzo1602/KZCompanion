using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DroidDeck.Services;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Actions.Providers
{
    public class SteamProvider : IActionProvider
    {
        private readonly ILogger<SteamProvider> _logger;

        public SteamProvider(ILogger<SteamProvider> logger) => _logger = logger;

        public string Type => "steam";

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Steam game",
            Icon = "sports_esports",
            Category = "System",
            Editor = "steam",
            Fields =
            {
                ActionField.Dynamic("appId", "Game", "/api/steam/games"),
            },
        };

        public Task ExecuteAsync(ActionContext c)
        {
            var appId = c.Get("appId");
            if (!Regex.IsMatch(appId, @"^\d{1,10}$"))
            {
                _logger.LogWarning("Steam: appid invalido '{AppId}'", appId);
                return Task.CompletedTask;
            }
            Process.Start(new ProcessStartInfo($"steam://rungameid/{appId}") { UseShellExecute = true })?.Dispose();
            return Task.CompletedTask;
        }
    }
}
