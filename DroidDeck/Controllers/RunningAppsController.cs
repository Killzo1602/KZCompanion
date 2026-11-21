using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DroidDeck.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroidDeck.Controllers
{
    /// <summary>
    /// Aplicativos abertos no PC (secao "Running now" da tela Apps). Tudo e por id de aplicativo
    /// (hash do .exe) e id de janela (HWND + processo): o servidor reenumera e confere a identidade
    /// antes de cada acao. Nunca recebe caminho, titulo ou PID do cliente.
    /// </summary>
    [Route("api/running-apps")]
    [ApiController]
    [Authorize]
    public class RunningAppsController : ControllerBase
    {
        private static readonly Regex AppId = new("^(pid[0-9]{1,10}|[0-9a-f]{12})$", RegexOptions.Compiled);
        private static readonly Regex WindowId = new(@"^[0-9a-f]{1,16}\.[0-9]{1,10}$", RegexOptions.Compiled);

        private readonly RunningAppsService _apps;

        public RunningAppsController(RunningAppsService apps) => _apps = apps;

        [HttpGet]
        public IActionResult List() => Ok(_apps.Payload());

        [HttpGet("{id}/icon")]
        public IActionResult Icon(string id)
        {
            if (!AppId.IsMatch(id)) return BadRequest(new { error = "invalid_id" });
            var png = _apps.IconFor(id);
            if (png == null) return NotFound();
            Response.Headers["Cache-Control"] = "private, max-age=600";
            return File(png, "image/png");
        }

        [HttpPost("{id}/focus")]
        public async Task<IActionResult> Focus(string id, [FromQuery] string? windowId = null) =>
            !Valid(id, windowId) ? BadRequest(new { error = "invalid_id" }) : Map(await _apps.FocusAsync(id, windowId));

        [HttpPost("{id}/minimize")]
        public IActionResult Minimize(string id, [FromQuery] string? windowId = null) =>
            !Valid(id, windowId) ? BadRequest(new { error = "invalid_id" }) : Map(_apps.Minimize(id, windowId));

        /// <summary>Fecha pedindo ao app (WM_CLOSE). Exige confirm=true: o celular pergunta antes.</summary>
        [HttpPost("{id}/close")]
        public async Task<IActionResult> Close(string id, [FromQuery] string? windowId = null, [FromQuery] bool confirm = false)
        {
            if (!Valid(id, windowId)) return BadRequest(new { error = "invalid_id" });
            if (!confirm) return BadRequest(new { error = "confirmation_required" });
            return Map(await _apps.CloseAsync(id, windowId));
        }

        private static bool Valid(string id, string? windowId) =>
            AppId.IsMatch(id) && (string.IsNullOrEmpty(windowId) || WindowId.IsMatch(windowId));

        private IActionResult Map(AppActionResult r) => r.Outcome switch
        {
            AppActionOutcome.Ok => Ok(new { ok = true, closed = true }),
            AppActionOutcome.StillOpen => Ok(new { ok = true, closed = false }),
            AppActionOutcome.AppGone => NotFound(new { error = "app_gone" }),
            AppActionOutcome.WindowGone => NotFound(new { error = "window_gone" }),
            AppActionOutcome.Elevated => Conflict(new { error = "elevated" }),
            AppActionOutcome.FocusDenied => Conflict(new { error = "focus_denied" }),
            _ => StatusCode(500, new { error = "failed", detail = r.Detail }),
        };
    }
}
