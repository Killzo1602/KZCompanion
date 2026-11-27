using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Lib;
using Xunit;

namespace DroidDeck.Tests
{
    public class UsbBridgeTests
    {
        [Fact]
        public void ParseDevices_SoOsProntos()
        {
            var output = "* daemon not running; starting now at tcp:5037\r\n" +
                         "* daemon started successfully\r\n" +
                         "List of devices attached\r\n" +
                         "9490bcb5\tdevice\r\n" +
                         "emulator-5554\toffline\r\n" +
                         "R58M123\tunauthorized\r\n" +
                         "192.168.0.5:5555\tdevice\r\n\r\n";
            Assert.Equal(new[] { "9490bcb5", "192.168.0.5:5555" }, UsbBridge.ParseDevices(output));
        }

        [Fact]
        public async Task UmCelularComErro_NaoImpedeOsOutros()
        {
            var failed = new System.Collections.Generic.List<string>();
            var done = await UsbBridge.ForwardAllAsync(new[] { "a", "b", "c" },
                serial => serial == "b" ? throw new TimeoutException() : Task.CompletedTask,
                (serial, _) => failed.Add(serial), CancellationToken.None);
            Assert.Equal(new[] { "a", "c" }, System.Linq.Enumerable.OrderBy(done, x => x));
            Assert.Equal(new[] { "b" }, failed);
        }

        [Fact]
        public async Task ProcessoTravado_EhEncerradoComPrazo()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(() =>
                UsbBridge.RunAsync("ping", "-n 30 127.0.0.1", CancellationToken.None, TimeSpan.FromMilliseconds(500)));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task CodigoDeSaidaComErro_EhFalha()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                UsbBridge.RunAsync("cmd", "/c exit 3", CancellationToken.None));
        }

        [Fact]
        public void HasReverse_ProcuraAPorta()
        {
            Assert.True(UsbBridge.HasReverse("UsbFfs tcp:4787 tcp:4787\n", 4787));
            Assert.False(UsbBridge.HasReverse("UsbFfs tcp:8081 tcp:8081\n", 4787));
            Assert.False(UsbBridge.HasReverse("", 4787));
        }

        [Fact]
        public void OwnerOfLoopbackClient_AchaOProcessoDoCliente()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var serverPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, serverPort);
                using var accepted = listener.AcceptTcpClient();
                var clientPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

                Assert.Equal(Environment.ProcessId, UsbBridge.OwnerOfLoopbackClient(clientPort, serverPort));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void IsForwarded_ConexaoDeForaDoLoopbackNuncaE()
        {
            Assert.False(UsbBridge.IsForwarded(IPAddress.Parse("192.168.0.10"), 50000));
            Assert.False(UsbBridge.IsForwarded(null, 50000));
        }
    }
}
