using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DroidDeck.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DroidDeck.Tests
{
    public class KzEndpointsTests : IClassFixture<WebApplicationFactory<DroidDeck.Program>>
    {
        private readonly WebApplicationFactory<DroidDeck.Program> _factory;

        public KzEndpointsTests(WebApplicationFactory<DroidDeck.Program> factory) => _factory = factory;

        private HttpClient Authed()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-KEY", ApiKeyProvider.GetKey());
            return client;
        }

        [Theory]
        [InlineData(LibreHardwareMonitor.Hardware.SensorType.SmallData, 16376f, true)]
        [InlineData(LibreHardwareMonitor.Hardware.SensorType.Clock, 10501f, true)]
        [InlineData(LibreHardwareMonitor.Hardware.SensorType.Temperature, 65261f, false)]
        [InlineData(LibreHardwareMonitor.Hardware.SensorType.SmallData, 0f, false)]
        public void Sensores_TetoDependeDaUnidade(LibreHardwareMonitor.Hardware.SensorType type, float value, bool kept) =>
            Assert.Equal(kept ? value : (float?)null, DroidDeck.Services.HardwareSensors.Valid(value, type));

        [Fact]
        public async Task SaidaDeAudio_DesconhecidaNaoMudaNada()
        {
            var body = new StringContent("{\"id\":\"nao-existe\"}", System.Text.Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/audio/outputs")).StatusCode);
            var status = (await Authed().PutAsync("/api/audio/output", body)).StatusCode;
            Assert.Contains(status, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });
        }

        [Fact]
        public async Task SystemInfo_ExigeAChave_EResponde()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/system/info")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Authed().GetAsync("/api/system/info")).StatusCode);
        }

        [Fact]
        public async Task AppsAbertos_ContinuaDisponivel()
        {
            Assert.Equal(HttpStatusCode.OK, (await Authed().GetAsync("/api/system/apps")).StatusCode);
        }

        [Theory]
        [InlineData("/api/StreamDeck/profiles")]
        [InlineData("/api/StreamDeck/layout")]
        [InlineData("/api/slider?target=output")]
        [InlineData("/api/triggers")]
        [InlineData("/api/backup")]
        [InlineData("/api/info")]
        public async Task RotasDoDeckAntigo_Sairam(string path)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Authed().GetAsync(path)).StatusCode);
        }

        [Theory]
        [InlineData("/api/apps")]
        [InlineData("/api/audio")]
        [InlineData("/api/sensors")]
        [InlineData("/api/ai-usage")]
        [InlineData("/api/ai-sessions")]
        [InlineData("/api/dev/docker")]
        [InlineData("/api/dev/github")]
        [InlineData("/api/discord/state")]
        [InlineData("/api/steam/games")]
        public async Task RotasDoKz_ContinuamExigindoAChave(string path)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(path)).StatusCode);
        }
    }
}
