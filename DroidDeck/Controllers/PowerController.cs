using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroidDeck.Controllers
{
    public class PowerRequest
    {
        public string? Operation { get; set; }
        /// <summary>Reiniciar e desligar exigem confirmacao explicita (o app mostra o dialogo).</summary>
        public bool Confirm { get; set; }
    }

    /// <summary>Energia do Windows pelo celular. Nada aqui executa codigo escolhido por quem chama.</summary>
    [Route("api/power")]
    [ApiController]
    [Authorize]
    public class PowerController : ControllerBase
    {
        [HttpPost]
        public IActionResult Run([FromBody] PowerRequest body)
        {
            switch ((body.Operation ?? "").ToLowerInvariant())
            {
                case "lock":
                    LockWorkStation();
                    return Ok(new { done = "lock" });
                case "sleep":
                    // false,false,false = suspender (nao hibernar), sem forcar.
                    SetSuspendState(false, false, false);
                    return Ok(new { done = "sleep" });
                case "restart":
                    if (!body.Confirm) return BadRequest(new { error = "Confirmação necessária." });
                    Shutdown("/r");
                    return Ok(new { done = "restart" });
                case "shutdown":
                    if (!body.Confirm) return BadRequest(new { error = "Confirmação necessária." });
                    Shutdown("/s");
                    return Ok(new { done = "shutdown" });
                case "cancel":
                    Shutdown("/a");
                    return Ok(new { done = "cancel" });
                default:
                    return BadRequest(new { error = "Operação desconhecida." });
            }
        }

        // 10 s de margem: da para cancelar pelo proprio app (ou 'shutdown /a') se foi um toque errado.
        private static void Shutdown(string flag) =>
            Process.Start(new ProcessStartInfo("shutdown.exe", flag == "/a" ? "/a" : $"{flag} /t 10")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });

        [DllImport("user32.dll")] private static extern bool LockWorkStation();
        [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool force, bool wakeupEventsDisabled);
    }
}
