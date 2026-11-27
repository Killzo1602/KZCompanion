using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Services
{
    public class TuyaAutoConnect : IHostedService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

        private readonly TuyaService _tuya;
        private readonly ILogger<TuyaAutoConnect> _logger;
        private CancellationTokenSource? _cts;
        private int _consecutiveFailures;

        public TuyaAutoConnect(TuyaService tuya, ILogger<TuyaAutoConnect> logger)
        {
            _tuya = tuya;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => WatchdogAsync(_cts.Token));
            return Task.CompletedTask;
        }

        private async Task WatchdogAsync(CancellationToken ct)
        {
            try { await Task.Delay(3000, ct); } catch { return; }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_tuya.HasSavedSession() && !_tuya.Connected)
                    {
                        await _tuya.ConnectAsync();
                        _consecutiveFailures = 0;
                        _logger.LogInformation("Tuya auto-connect: conectado.");
                    }
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;
                    if (_consecutiveFailures > 1)
                        _logger.LogWarning("Tuya auto-connect: {N} tentativas seguidas falharam ({Msg})",
                            _consecutiveFailures, ex.Message);
                    else
                        _logger.LogDebug("Tuya auto-connect: {Msg}", ex.Message);
                }

                try { await Task.Delay(Interval, ct); } catch { return; }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try { _cts?.Cancel(); } catch { }
            await _tuya.DisposeAsync();
        }
    }
}
