using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using DroidDeck.Models;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Actions.Providers
{
    public class MultiProvider : IActionProvider
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        private readonly ILogger<MultiProvider> _logger;
        private readonly Services.Conditions _conditions;

        public MultiProvider(ILogger<MultiProvider> logger, Services.Conditions conditions)
        {
            _logger = logger;
            _conditions = conditions;
        }

        public string Type => "multi";

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Multi-action",
            Icon = "playlist_play",
            Category = "Utilities",
            Description = "Several steps in sequence, with a wait between them.",
            Editor = "multi",
        };

        public async Task ExecuteAsync(ActionContext c)
        {
            var stepsJson = c.Get("steps");
            if (string.IsNullOrWhiteSpace(stepsJson)) return;

            List<MultiStep>? steps;
            try
            {
                steps = JsonSerializer.Deserialize<List<MultiStep>>(stepsJson, JsonOpts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Multi-ação: JSON de passos inválido");
                return;
            }
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (string.IsNullOrWhiteSpace(step.Type)) continue;
                if (string.Equals(step.Type, Type, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (step.DelayMs > 0)
                    await Task.Delay(step.DelayMs);
                if (!_conditions.Evaluate(step.If)) continue;
                await c.ExecuteAsync(new DeckAction
                {
                    Type = step.Type!,
                    Parameters = step.Parameters ?? new Dictionary<string, string>()
                });
            }
        }
    }
}
