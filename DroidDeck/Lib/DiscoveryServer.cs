using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Lib
{
    internal class DiscoveryServer : BackgroundService
    {
        private const int DiscoveryPort = 7573;
        private UdpClient? udpServer;
        private readonly ILogger<DiscoveryServer> _logger;

        public DiscoveryServer(ILogger<DiscoveryServer> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            try
            {
                udpServer = new UdpClient(DiscoveryPort);
                _logger.LogInformation("[Discovery] Servidor iniciado na porta {Port}", DiscoveryPort);
                string computerName = Environment.MachineName;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var receiveTask = udpServer.ReceiveAsync();
                        var completed = await Task.WhenAny(receiveTask, Task.Delay(Timeout.Infinite, token));

                        if (completed != receiveTask)
                        {
                            break;
                        }

                        var result = receiveTask.Result;
                        string dataReceived = Encoding.ASCII.GetString(result.Buffer);
                        _logger.LogDebug("[Discovery] Recebido '{Data}' de {Remote}", dataReceived, result.RemoteEndPoint);

                        if ("DroidDeckDiscoveryRequest".Equals(dataReceived))
                        {
                            string ipAddress = NetworkInfo.GetLocalIpFor(result.RemoteEndPoint.Address);
                            string responseMessage = System.Text.Json.JsonSerializer.Serialize(
                                new { ip = ipAddress, name = computerName });
                            byte[] sendBytes = Encoding.UTF8.GetBytes(responseMessage);
                            await udpServer.SendAsync(sendBytes, sendBytes.Length, result.RemoteEndPoint);
                            _logger.LogDebug("[Discovery] Resposta enviada para {Remote}", result.RemoteEndPoint);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Discovery] Erro transitorio no listener; continuando");
                        try { await Task.Delay(500, token); } catch { break; }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Discovery server");
            }
            finally
            {
                try { udpServer?.Close(); udpServer?.Dispose(); } catch { }
            }
        }
    }
}
