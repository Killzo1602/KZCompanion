using System;
using System.Collections.Generic;
using System.Linq;
using DroidDeck.Audio;
using DroidDeck.Lib;
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

    public class AudioOutputRequest
    {
        public string? Id { get; set; }
    }

    public class DeafenRequest
    {
        public bool Enabled { get; set; }
    }

    [Route("api/audio")]
    [ApiController]
    [Authorize]
    public class AudioController : ControllerBase
    {
        private readonly AudioMixerService _audio;
        public AudioController(AudioMixerService audio) => _audio = audio;

        [HttpGet]
        public IActionResult Get() => Ok(_audio.GetState(withIcons: true));

        [HttpGet("outputs")]
        public IActionResult Outputs()
        {
            try
            {
                var current = DefaultAudioEndpoint.GetDefault(input: false);
                return Ok(DefaultAudioEndpoint.List(input: false)
                    .Select(d => new { id = d.Id, name = d.Name, isDefault = d.Id == current }));
            }
            catch (Exception ex)
            {
                Log.Warning($"Saidas de audio: {ex.Message}");
                return Ok(Array.Empty<object>());
            }
        }

        [HttpPut("output")]
        public IActionResult Output([FromBody] AudioOutputRequest body)
        {
            if (string.IsNullOrEmpty(body.Id)) return BadRequest(new { error = "id is required." });
            try
            {
                if (DefaultAudioEndpoint.List(input: false).All(d => d.Id != body.Id))
                    return NotFound(new { error = "That output is no longer available." });
                DefaultAudioEndpoint.Set(body.Id);
                return Ok(_audio.GetState(false));
            }
            catch (Exception ex)
            {
                Log.Warning($"Troca de saida de audio: {ex.Message}");
                return BadRequest(new { error = "Windows did not accept the change." });
            }
        }

        [HttpPut("master")]
        public IActionResult Master([FromBody] AudioChange body)
        {
            if (body.Volume == null && body.Mute == null) return BadRequest(new { error = "Nothing to change." });
            var (ok, error) = _audio.SetMaster(body.Volume, body.Mute);
            return ok ? Ok(_audio.GetState(false)) : BadRequest(new { error });
        }

        [HttpPut("mic")]
        public IActionResult Mic([FromBody] AudioChange body)
        {
            if (body.Mute == null) return BadRequest(new { error = "mute is required." });
            var (ok, error) = _audio.SetMic(body.Mute.Value);
            return ok ? Ok(_audio.GetState(false)) : BadRequest(new { error });
        }

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
