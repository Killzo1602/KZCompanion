using System.Threading.Tasks;
using DroidDeck.Actions;

namespace DroidDeck.Integrations
{
    public class MqttProvider : IStatefulActionProvider
    {
        public const string TypeName = "mqtt";

        private readonly MqttIntegration _mqtt;

        public MqttProvider(MqttIntegration mqtt) => _mqtt = mqtt;

        public string Type => TypeName;
        public string StateEvent => MqttIntegration.Event;
        public object GetState() => _mqtt.GetState();

        public ActionSchema Describe() => new()
        {
            Type = TypeName,
            Title = "MQTT",
            Icon = "hub",
            Category = "Home",
            Description = "Publishes a message to a topic on your MQTT broker (Mosquitto, Home Assistant, Node-RED, Zigbee2MQTT...).",
            Settings = "mqtt",
            StateEvent = StateEvent,
            Fields =
            {
                ActionField.Text("topic", "Topic", "home/living-room/light/set", required: true),
                ActionField.Multiline("payload", "Message", "ON", "Text or JSON. Can be empty."),
                ActionField.Select("qos", "QoS",
                    ("0", "0 — at most once"),
                    ("1", "1 — at least once"),
                    ("2", "2 — exactly once")),
                ActionField.Toggle("retain", "Retain", false, "The broker keeps the topic's last message for whoever subscribes later."),
            },
        };

        public Task ExecuteAsync(ActionContext ctx) =>
            _mqtt.PublishAsync(ctx.Get("topic"), ctx.Get("payload"), ctx.Int("qos", 0),
                ctx.Get("retain").Equals("true", System.StringComparison.OrdinalIgnoreCase));
    }
}
