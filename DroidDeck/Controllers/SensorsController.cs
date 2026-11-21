using DroidDeck.Models;
using DroidDeck.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroidDeck.Controllers
{
    /// <summary>Leitura pontual dos sensores (o app recebe o fluxo pelo SignalR; isto serve a diagnóstico).</summary>
    [Route("api/sensors")]
    [ApiController]
    [Authorize]
    public class SensorsController : ControllerBase
    {
        private readonly HardwareSensors _sensors;
        public SensorsController(HardwareSensors sensors) => _sensors = sensors;

        [HttpGet]
        public IActionResult Get()
        {
            var s = new SystemStats();
            _sensors.Fill(s);
            return Ok(s);
        }
    }
}
