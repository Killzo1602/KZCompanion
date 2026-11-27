using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DroidDeck.Hubs;
using DroidDeck.Lib;
using DroidDeck.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DroidDeck.Controllers
{
    public class AppOrder
    {
        public List<string>? Ids { get; set; }
    }

    public class AppUpsert
    {
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? Group { get; set; }
        public bool Favorite { get; set; }
        public string? IconBase64 { get; set; }
    }

    [Route("api/apps")]
    [ApiController]
    [Authorize]
    public class AppsController : ControllerBase
    {
        private readonly AppLibrary _library;
        private readonly InstalledApps _installed;
        private readonly IHubContext<DeckHub> _hub;

        public AppsController(AppLibrary library, InstalledApps installed, IHubContext<DeckHub> hub)
        {
            _library = library;
            _installed = installed;
            _hub = hub;
        }

        private bool IsLocal => ActionPolicy.IsLocal(HttpContext.Connection);

        private object Dto(AppEntry a) => new
        {
            a.Id,
            a.Name,
            a.Group,
            a.Favorite,
            a.Kind,
            Path = IsLocal ? a.Kind switch
            {
                "packaged" => a.AppId,
                "steam" => "steam://rungameid/" + a.AppId,
                "battlenet" => "Battle.net: " + a.AppId,
                _ => a.Path,
            } : null,
            IconBase64 = _library.IconBase64(a.Id),
        };

        [HttpGet]
        public IActionResult List() => Ok(_library.List().Select(Dto));

        [HttpPost("{id}/launch")]
        public IActionResult Launch(string id)
        {
            var app = _library.Get(id);
            if (app == null) return NotFound(new { error = "Aplicativo não encontrado." });
            if (app.Kind == "steam")
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(app.AppId ?? "", @"^\d{1,10}$")) return BadRequest(new { error = "Invalid Steam App ID." });
                try
                {
                    Process.Start(new ProcessStartInfo("steam://rungameid/" + app.AppId) { UseShellExecute = true });
                    return Ok(new { launched = app.Name });
                }
                catch (Exception ex)
                {
                    Log.Warning($"Falha ao abrir '{app.Name}' pela Steam: {ex.Message}");
                    return StatusCode(500, new { error = ex.Message });
                }
            }
            if (app.Kind == "battlenet")
            {
                var launcher = BattleNetGames.FindLauncher();
                if (!System.Text.RegularExpressions.Regex.IsMatch(app.AppId ?? "", @"^[A-Za-z0-9_]{2,16}$") || launcher == null)
                    return BadRequest(new { error = "Battle.net is not available." });
                try
                {
                    var psi = new ProcessStartInfo(launcher) { UseShellExecute = false };
                    psi.ArgumentList.Add("--exec=launch " + app.AppId);
                    Process.Start(psi);
                    return Ok(new { launched = app.Name });
                }
                catch (Exception ex)
                {
                    Log.Warning($"Falha ao abrir '{app.Name}' pela Battle.net: {ex.Message}");
                    return StatusCode(500, new { error = ex.Message });
                }
            }
            if (app.Kind == "packaged")
            {
                if (!InstalledApps.IsValidAumid(app.AppId)) return BadRequest(new { error = "Invalid application id." });
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + app.AppId) { UseShellExecute = false });
                    return Ok(new { launched = app.Name });
                }
                catch (Exception ex)
                {
                    Log.Warning($"Falha ao abrir '{app.Name}': {ex.Message}");
                    return StatusCode(500, new { error = ex.Message });
                }
            }
            if (!AppLibrary.TryValidatePath(app.Path, out var full, out var error))
                return BadRequest(new { error });
            try
            {
                Process.Start(new ProcessStartInfo(full)
                {
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(full) ?? "",
                });
                return Ok(new { launched = app.Name });
            }
            catch (Exception ex)
            {
                Log.Warning($"Falha ao abrir '{app.Name}': {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("installed")]
        public IActionResult Installed([FromQuery] bool refresh = false)
        {
            if (!IsLocal) return StatusCode(403, new { error = "Só pelo configurador no PC." });
            var have = _library.Identities();
            return Ok(_installed.List(refresh).Select(i => new
            {
                key = i.Key,
                name = i.Name,
                kind = i.Kind,
                source = i.Source,
                detail = i.Detail,
                added = have.Contains(i.Identity),
            }));
        }

        [HttpGet("installed/{key}/icon")]
        public IActionResult InstalledIcon(string key)
        {
            if (!IsLocal) return StatusCode(403, new { error = "Só pelo configurador no PC." });
            var item = _installed.Find(key);
            if (item == null) return NotFound();
            return Ok(new { iconBase64 = _installed.IconBase64(item) });
        }

        [HttpPost("installed/{key}/add")]
        public async Task<IActionResult> AddInstalled(string key, [FromBody] AppUpsert? body)
        {
            if (!IsLocal) return StatusCode(403, new { error = "Só pelo configurador no PC." });
            var item = _installed.Find(key);
            if (item == null) return NotFound(new { error = "Application not found. Refresh the list." });
            var dup = _library.FindByIdentity(item.Identity);
            if (dup != null) return Conflict(new { error = $"\"{dup.Name}\" is already in your library.", id = dup.Id });
            var entry = new AppEntry { Name = item.Name, Group = (body?.Group ?? "").Trim(), Favorite = body?.Favorite ?? false, Kind = item.Kind };
            if (item.Kind is "steam" or "battlenet")
            {
                entry.AppId = item.Path;
                entry.Path = "";
            }
            else if (item.Kind == "packaged")
            {
                if (!InstalledApps.IsValidAumid(item.Path)) return BadRequest(new { error = "Invalid application id." });
                entry.AppId = item.Path;
                entry.Path = "";
            }
            else
            {
                if (!AppLibrary.TryValidatePath(item.Path, out var full, out var error)) return BadRequest(new { error });
                entry.Path = full;
            }
            var saved = _library.Add(entry, InstalledApps.IconFor(item));
            await _hub.Clients.All.SendAsync("ReceiveAppsChanged");
            return Ok(Dto(saved));
        }

        [HttpPost("pick")]
        public async Task<IActionResult> Pick()
        {
            if (!IsLocal) return StatusCode(403, new { error = "Só pelo configurador no PC." });
            var chosen = await Task.Run(ShowFileDialog);
            if (chosen == null) return NoContent();
            return Ok(Preview(chosen));
        }

        [HttpPost("preview")]
        public IActionResult PreviewPath([FromBody] AppUpsert body)
        {
            if (!IsLocal) return StatusCode(403, new { error = "Só pelo configurador no PC." });
            if (!AppLibrary.TryValidatePath(body.Path, out var full, out var error)) return BadRequest(new { error });
            return Ok(Preview(full));
        }

        private static object Preview(string full)
        {
            var png = AppLibrary.ExtractIconPng(full);
            return new
            {
                path = full,
                name = System.IO.Path.GetFileNameWithoutExtension(full),
                iconBase64 = png == null ? null : Convert.ToBase64String(png),
            };
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] AppUpsert body)
        {
            if (!IsLocal) return StatusCode(403, new { error = "Só pelo configurador no PC." });
            if (!AppLibrary.TryValidatePath(body.Path, out var full, out var error)) return BadRequest(new { error });
            var dup = _library.FindDuplicate(full);
            if (dup != null) return Conflict(new { error = $"\"{dup.Name}\" is already in your library.", id = dup.Id });
            var name = string.IsNullOrWhiteSpace(body.Name) ? System.IO.Path.GetFileNameWithoutExtension(full) : body.Name.Trim();
            var entry = _library.Add(
                new AppEntry { Name = name, Path = full, Group = (body.Group ?? "").Trim(), Favorite = body.Favorite },
                AppLibrary.NormalizeUserIcon(body.IconBase64),
                customIcon: !string.IsNullOrWhiteSpace(body.IconBase64));
            await _hub.Clients.All.SendAsync("ReceiveAppsChanged");
            return Ok(Dto(entry));
        }

        [HttpPut("order")]
        public async Task<IActionResult> Reorder([FromBody] AppOrder body)
        {
            if (!IsLocal) return StatusCode(403, new { error = "Só pelo configurador no PC." });
            if (body.Ids == null || body.Ids.Count > 2000) return BadRequest(new { error = "Invalid order." });
            _library.Reorder(body.Ids);
            await _hub.Clients.All.SendAsync("ReceiveAppsChanged");
            return NoContent();
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] AppUpsert body)
        {
            if (!IsLocal) return StatusCode(403, new { error = "Só pelo configurador no PC." });
            var path = "";
            if (!string.IsNullOrWhiteSpace(body.Path))
            {
                if (!AppLibrary.TryValidatePath(body.Path, out path, out var error)) return BadRequest(new { error });
            }
            var changes = new AppEntry
            {
                Name = (body.Name ?? "").Trim(),
                Path = path,
                Group = (body.Group ?? "").Trim(),
                Favorite = body.Favorite,
            };
            if (changes.Name.Length == 0) return BadRequest(new { error = "Dê um nome ao aplicativo." });
            var updated = _library.Update(id, changes, AppLibrary.NormalizeUserIcon(body.IconBase64));
            if (updated == null) return NotFound();
            await _hub.Clients.All.SendAsync("ReceiveAppsChanged");
            return Ok(Dto(updated));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            if (!IsLocal) return StatusCode(403, new { error = "Só pelo configurador no PC." });
            if (!_library.Remove(id)) return NotFound();
            await _hub.Clients.All.SendAsync("ReceiveAppsChanged");
            return NoContent();
        }

        private static string? ShowFileDialog()
        {
            string? result = null;
            var t = new Thread(() =>
            {
                using var owner = new Form { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-32000, -32000), Size = new System.Drawing.Size(1, 1) };
                owner.Show();
                using var dlg = new OpenFileDialog
                {
                    Title = "KZ Companion — escolha um aplicativo",
                    Filter = "Aplicativos e atalhos (*.exe;*.lnk)|*.exe;*.lnk",
                    CheckFileExists = true,
                    DereferenceLinks = false,
                };
                if (dlg.ShowDialog(owner) == DialogResult.OK) result = dlg.FileName;
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            return result;
        }
    }
}
