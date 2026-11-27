using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace DroidDeck.Lib
{
    public class AppUpdater
    {
        public const string RepoUrl = "https://github.com/Killzo1602/KZCompanion";

        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

        private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, false));
        private readonly SemaphoreSlim _lock = new(1, 1);
        private UpdateInfo? _ready;

        public event Action<string>? UpdateReady;

        public bool IsInstalled => _manager.IsInstalled;

        public string? CurrentVersion => _manager.CurrentVersion?.ToString();

        public string? PendingVersion => _ready?.TargetFullRelease.Version.ToString();

        public void StartBackgroundChecks(CancellationToken token)
        {
            if (!IsInstalled) return;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30), token); } catch { return; }
                while (!token.IsCancellationRequested)
                {
                    await CheckAndDownloadAsync();
                    try { await Task.Delay(CheckInterval, token); } catch { return; }
                }
            }, token);
        }

        public async Task<string?> CheckAndDownloadAsync()
        {
            if (!IsInstalled) return null;
            await _lock.WaitAsync();
            try
            {
                if (_ready != null) return PendingVersion;

                var info = await _manager.CheckForUpdatesAsync();
                if (info == null) return null;

                await _manager.DownloadUpdatesAsync(info);
                _ready = info;
                var version = PendingVersion!;
                Log.Info($"Atualizacao {version} baixada; aplica ao reiniciar.");
                UpdateReady?.Invoke(version);
                return version;
            }
            catch (Exception ex)
            {
                Log.Warning($"Atualizacao: falha ao checar/baixar ({ex.Message})");
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public void ApplyAndRestart()
        {
            if (_ready == null) return;
            _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
        }
    }
}
