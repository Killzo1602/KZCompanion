using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DroidDeck.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DroidDeck.Tests
{
    /// <summary>
    /// O app Android confere a chave em /api/system/info e nao depende mais de nenhuma rota do
    /// antigo deck de botoes.
    /// </summary>
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
        [InlineData("/api/discord/state")]
        [InlineData("/api/steam/games")]
        public async Task RotasDoKz_ContinuamExigindoAChave(string path)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(path)).StatusCode);
        }
    }
}
