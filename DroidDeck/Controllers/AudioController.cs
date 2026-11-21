using System.Collections.Generic;
using DroidDeck.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroidDeck.Controllers
{
    public class AudioChange
    {
        public int? Volume { get; set; }
        public bool? Mute { get; set; }
    }

    public class AudioGroupChange
    {
        public List<string>? Ids { get; set; }
        public int? Volume { get; set; }
        public bool? Mute { get; set; }
    }

    public class DeafenRequest
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Mixer do Windows para o celular. O Windows e a fonte da verdade: cada comando e validado
    /// (0-100, sessao existente) e aplicado aqui; o estado novo volta pelo hub (ReceiveAudioState).
    /// </summary>
    [Route("api/audio")]
    [ApiController]
    [Authorize]
    public class AudioController : ControllerBase
    {
        private readonly AudioMixerService _audio;
        public AudioController(AudioMixerService audio) => _audio = audio;

        [HttpGet]
        public IActionResult Get() => Ok(_audio.GetState(withIcons: true));

        [HttpPut("master")]
        public IActionResult Master([FromBody] AudioChange body)
        {
            if (body.Volume == null && body.Mute == null) return BadRequest(new { error = "Nothing to change." });
            var (ok, error) = _audio.SetMaster(body.Volume, body.Mute);
            return ok ? Ok(_audio.GetState(false)) : BadRequest(new { error });
        }

        /// <summary>Mudo do microfone padrao (so ele; a saida e o volume nao mudam).</summary>
        [HttpPut("mic")]
        public IActionResult Mic([FromBody] AudioChange body)
        {
            if (body.Mute == null) return BadRequest(new { error = "mute is required." });
            var (ok, error) = _audio.SetMic(body.Mute.Value);
            return ok ? Ok(_audio.GetState(false)) : BadRequest(new { error });
        }

        /// <summary>Volume/mudo de varias sessoes (o grupo de um aplicativo), cada uma pelo seu id original.</summary>
        [HttpPut("sessions")]
        public IActionResult Sessions([FromBody] AudioGroupChange body)
        {
            if (body.Ids == null) return BadRequest(new { error = "ids are required." });
            if (body.Volume == null && body.Mute == null) return BadRequest(new { error = "Nothing to change." });
            var (ok, error, notFound, _) = _audio.SetSessions(body.Ids, body.Volume, body.Mute);
            if (notFound) return NotFound(new { error });
            return ok ? Ok(_audio.GetState(false)) : BadRequest(new { error });
        }

        [HttpPut("sessions/{id}")]
        public IActionResult Session(string id, [FromBody] AudioChange body)
        {
            if (body.Volume == null && body.Mute == null) return BadRequest(new { error = "Nothing to change." });
            var (ok, error, notFound) = _audio.SetSession(id, body.Volume, body.Mute);
            if (notFound) return NotFound(new { error });
            return ok ? Ok(_audio.GetState(false)) : BadRequest(new { error });
        }

        [HttpPost("deafen")]
        public IActionResult Deafen([FromBody] DeafenRequest body)
        {
            var (ok, error) = _audio.SetDeafen(body.Enabled);
            return ok ? Ok(_audio.GetState(false)) : BadRequest(new { error });
        }
    }
}
