using System.Linq;
using DroidDeck.Services;
using Microsoft.AspNetCore.Mvc;

namespace DroidDeck.Controllers
{
    [ApiController]
    public class LibraryController : ControllerBase
    {
        private readonly SteamLibrary _steam;

        public LibraryController(SteamLibrary steam) => _steam = steam;

        [HttpGet("api/steam/games")]
        public IActionResult Games() =>
            Ok(_steam.Games().Select(g => new { value = g.AppId, label = g.Name }));

        [HttpGet("api/steam/icon/{appId}")]
        public IActionResult Icon(string appId) =>
            _steam.IconBase64(appId) is { } icon ? Ok(new { iconBase64 = icon }) : NotFound();

        [HttpGet("api/system/apps")]
        public IActionResult Apps() => Ok(ForegroundWatcher.RunningApps());

        [HttpGet("api/system/info")]
        public IActionResult Info() => Ok(new
        {
            name = System.Environment.MachineName,
            version = typeof(LibraryController).Assembly.GetName().Version?.ToString(3),
        });
    }
}
