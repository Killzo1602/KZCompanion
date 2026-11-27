using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DroidDeck.Lib;
using DroidDeck.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroidDeck.Controllers
{
    [Route("api/ai-usage")]
    [ApiController]
    [Authorize]
    public class AiUsageController : ControllerBase
    {
        private readonly AiUsageService _ai;
        public AiUsageController(AiUsageService ai) => _ai = ai;

        public record Limit(string Label, double Percent, DateTimeOffset? ResetsAt, int WindowSeconds);

        public record Provider(string Id, string Name, bool Installed, IReadOnlyList<Limit> Limits,
            long? TokensToday, long? TokensWeek, string? Error);

        [HttpGet]
        public async Task<IActionResult> Get() => Ok(new
        {
            providers = await Task.WhenAll(
                ReadAsync(AiUsageService.Claude, "Claude Code", AiUsageService.ClaudeDir),
                ReadAsync(AiUsageService.Codex, "Codex", AiUsageService.CodexDir)),
        });

        private async Task<Provider> ReadAsync(string id, string name, string dir)
        {
            if (!Directory.Exists(dir))
                return new Provider(id, name, false, Array.Empty<Limit>(), null, null, null);

            var limits = new List<Limit>();
            string? error = null;
            try
            {
                limits = Limits(await _ai.PlanAsync(id), DateTimeOffset.UtcNow).ToList();
            }
            catch (LocalizedException ex) { error = ex.Text.In("en"); }
            catch (Exception ex)
            {
                Log.Warning($"AI usage ({id}): {ex.Message}");
                error = "Limits are not available right now.";
            }

            long? today = null, week = null;
            try
            {
                var tokens = await _ai.TokensAsync(id);
                today = tokens.Today;
                week = tokens.Week;
            }
            catch (Exception ex) { Log.Warning($"AI tokens ({id}): {ex.Message}"); }

            return new Provider(id, name, true, limits, today, week, error);
        }

        public static IEnumerable<Limit> Limits(AiUsageService.Plan plan, DateTimeOffset now)
        {
            const int fiveHours = 5 * 3600, week = 7 * 24 * 3600;
            Limit Of(string label, AiUsageService.Window w, int seconds) => w.ResetsAt is { } reset && reset <= now
                ? new Limit(label, 0, null, seconds)
                : new Limit(label, w.Percent, w.ResetsAt, seconds);

            if (plan.Session != null) yield return Of("5-hour", plan.Session, fiveHours);
            if (plan.Week != null) yield return Of("Weekly", plan.Week, week);
            foreach (var m in plan.Models ?? Array.Empty<AiUsageService.ModelWindow>())
                yield return Of($"Weekly · {m.Model}", m.Window, week);
        }
    }
}
