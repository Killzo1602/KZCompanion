using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DroidDeck.Services.Tuya
{
    public class TuyaConfig
    {
        public string? UserCode { get; set; }
        public TuyaToken? Token { get; set; }

        public string ClientId { get; set; } = "HA_3y9q4ak7g4ephrvke";
        public string Schema { get; set; } = "haauthorize";

        public bool HasSession => Token != null && !string.IsNullOrEmpty(Token.AccessToken);
    }

    public class TuyaToken
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("uid")] public string Uid { get; set; } = "";
        [JsonPropertyName("terminal_id")] public string TerminalId { get; set; } = "";
        [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "";

        [JsonPropertyName("t")] public long IssuedAt { get; set; }

        [JsonPropertyName("expire_time")] public long ExpiresInSeconds { get; set; }

        [JsonIgnore] public long ExpiresAtMs => IssuedAt + ExpiresInSeconds * 1000;
    }

    public class TuyaHome
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class TuyaDevice
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string? ProductName { get; set; }
        public bool Online { get; set; }
        public string? Icon { get; set; }

        public Dictionary<string, object?> Status { get; set; } = new();

        public Dictionary<string, TuyaFunction> Functions { get; set; } = new();

        public Dictionary<string, string> DpIdToCode { get; set; } = new();

        public bool SupportLocal { get; set; }
    }

    public class TuyaFunction
    {
        public string Type { get; set; } = "";

        public string? Values { get; set; }
    }
}
