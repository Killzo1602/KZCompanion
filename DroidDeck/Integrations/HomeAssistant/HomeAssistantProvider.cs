using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Actions;
using DroidDeck.Hubs;
using DroidDeck.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Integrations
{
    public class HomeAssistantProvider : IStatefulActionProvider
    {
        public const string TypeName = "homeassistant";

        private readonly HomeAssistantIntegration _ha;

        public HomeAssistantProvider(HomeAssistantIntegration ha) => _ha = ha;

        public string Type => TypeName;
        public string StateEvent => HomeAssistantIntegration.Event;
        public object GetState() => _ha.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "Home Assistant",
            Icon = "home",
            Category = "Home",
            Description = "Home Assistant lights, plugs, scenes, scripts and sensors, with their real state.",
            Stateful = true,
            Settings = "homeassistant",
            StateEvent = StateEvent,
            Fields =
            {
                ActionField.Select("operation", "What to do",
                    ("toggle", "Toggle on/off"),
                    ("turn_on", "Turn on"),
                    ("turn_off", "Turn off"),
                    ("brightness", "Brightness (+/-)"),
                    ("scene", "Activate scene"),
                    ("script", "Run script"),
                    ("display", "Only show the state"),
                    ("service", "Call a service")),
                ActionField.Dynamic("entityId", "Entity", "/api/integrations/homeassistant/options/entities?operation={operation}", allowCustom: true),
                ActionField.Number("step", "Brightness step", 10, -100, 100, "%").When("operation", "brightness"),
                ActionField.Text("service", "Service", "light.turn_on", "domain.service", required: true).When("operation", "service"),
                ActionField.Multiline("data", "Data (JSON, optional)", "{\"brightness_pct\": 40}").When("operation", "service"),
            },
        };

        public async Task ExecuteAsync(ActionContext ctx)
        {
            var op = ctx.Operation("toggle");
            var entity = ctx.Get("entityId").Trim();
            var domain = entity.Contains('.') ? entity.Split('.')[0] : "homeassistant";
            switch (op)
            {
                case "display":
                    return;
                case "turn_on":
                case "turn_off":
                    var on = op == "turn_on";
                    if (domain == "cover") await _ha.CallServiceAsync("cover", on ? "open_cover" : "close_cover", entity);
                    else if (domain == "lock") await _ha.CallServiceAsync("lock", on ? "unlock" : "lock", entity);
                    else await _ha.CallServiceAsync(OnOffDomain(domain), op, entity);
                    break;
                case "brightness":
                    await _ha.CallServiceAsync("light", "turn_on", entity,
                        new System.Text.Json.Nodes.JsonObject { ["brightness_step_pct"] = ctx.Int("step", 10) });
                    break;
                case "scene":
                    await _ha.CallServiceAsync("scene", "turn_on", entity);
                    break;
                case "script":
                    await _ha.CallServiceAsync("script", "turn_on", entity);
                    break;
                case "service":
                    var parts = ctx.Get("service").Trim().Split('.', 2);
                    if (parts.Length != 2) throw new InvalidOperationException("Service in domain.service format");
                    var data = string.IsNullOrWhiteSpace(ctx.Get("data")) ? null
                        : System.Text.Json.Nodes.JsonNode.Parse(ctx.Get("data")) as System.Text.Json.Nodes.JsonObject;
                    await _ha.CallServiceAsync(parts[0], parts[1], entity.Length > 0 ? entity : null, data);
                    break;
                default:
                    if (domain is "scene" or "script") await _ha.CallServiceAsync(domain, "turn_on", entity);
                    else if (domain == "lock")
                        await _ha.CallServiceAsync("lock", _ha.Find(entity)?.State == "locked" ? "unlock" : "lock", entity);
                    else await _ha.CallServiceAsync(OnOffDomain(domain), "toggle", entity);
                    break;
            }
        }

        private static string OnOffDomain(string domain) =>
            domain is "light" or "switch" or "fan" or "input_boolean" or "media_player" or "climate" or "automation"
                or "siren" or "humidifier" or "cover" or "vacuum"
                ? domain
                : "homeassistant";
    }
}
