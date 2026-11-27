using Windows.Media.Control;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Microsoft.Extensions.Logging;

namespace DroidDeck.Services
{
    public class MediaControlService
    {
        private enum Origin
        {
            Background,

            Interactive
        }

        private readonly ILogger<MediaControlService> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private GlobalSystemMediaTransportControlsSessionManager? _manager;

        private static readonly TimeSpan BackgroundGateWait = TimeSpan.FromMilliseconds(500);

        private static readonly TimeSpan InteractiveGateWait = TimeSpan.FromSeconds(10);

        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        private int _consecutiveFailures;
        private long _circuitOpenUntilTicks;
        private const int FailuresToOpen = 3;
        private static readonly TimeSpan CircuitCooldown = TimeSpan.FromSeconds(60);

        public MediaControlService(ILogger<MediaControlService> logger)
        {
            _logger = logger;
        }

        private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource();
            var winner = await Task.WhenAny(task, Task.Delay(timeout, cts.Token)).ConfigureAwait(false);
            if (winner != task) return false;
            cts.Cancel();
            return true;
        }

        private async Task<T> RunAsync<T>(
            Func<GlobalSystemMediaTransportControlsSessionManager, Task<T>> op,
            T fallback,
            string what,
            Origin origin,
            TimeSpan? timeout = null)
        {
            var limit = timeout ?? DefaultTimeout;
            var interactive = origin == Origin.Interactive;

            if (Volatile.Read(ref _circuitOpenUntilTicks) > DateTime.UtcNow.Ticks)
            {
                if (!interactive)
                {
                    _logger.LogDebug("Mídia: '{What}' curto-circuitado — subsistema marcado como indisponível.", what);
                    return fallback;
                }
                _logger.LogInformation("Mídia: '{What}' tentado mesmo com o disjuntor aberto (veio do usuário).", what);
            }

            var gateWait = interactive ? InteractiveGateWait : BackgroundGateWait;
            if (!await _gate.WaitAsync(gateWait).ConfigureAwait(false))
            {
                if (interactive)
                    _logger.LogWarning(
                        "Mídia: '{What}' descartado — o portão ficou ocupado por {Secs}s. " +
                        "O broker de mídia do Windows provavelmente está travado.",
                        what, gateWait.TotalSeconds);
                else
                    _logger.LogDebug("Mídia: '{What}' descartado — outra operação ainda em voo.", what);
                return fallback;
            }

            try
            {
                var manager = _manager;
                if (manager == null)
                {
                    var request = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
                    if (!await CompletesWithinAsync(request, limit).ConfigureAwait(false))
                    {
                        _logger.LogWarning("Mídia: timeout ({Secs}s) obtendo o session manager.", limit.TotalSeconds);
                        NoteFailure("timeout no RequestAsync");
                        return fallback;
                    }
                    _manager = manager = await request.ConfigureAwait(false);
                }

                var task = op(manager);
                if (!await CompletesWithinAsync(task, limit).ConfigureAwait(false))
                {
                    _logger.LogWarning("Mídia: timeout ({Secs}s) em '{What}'; a chamada WinRT ficou pendurada.",
                        limit.TotalSeconds, what);
                    NoteFailure($"timeout em {what}");
                    return fallback;
                }

                var result = await task.ConfigureAwait(false);
                NoteSuccess();
                return result;
            }
            catch (Exception ex)
            {
                _manager = null;
                _logger.LogError(ex, "Mídia: falha em '{What}'", what);
                NoteFailure($"{ex.GetType().Name} 0x{ex.HResult:X8}");
                return fallback;
            }
            finally
            {
                _gate.Release();
            }
        }

        private void NoteSuccess()
        {
            if (_consecutiveFailures != 0 || Volatile.Read(ref _circuitOpenUntilTicks) != 0)
                _logger.LogInformation("Mídia: subsistema respondendo de novo.");
            _consecutiveFailures = 0;
            Volatile.Write(ref _circuitOpenUntilTicks, 0);
        }

        private void NoteFailure(string reason)
        {
            if (++_consecutiveFailures < FailuresToOpen) return;

            Volatile.Write(ref _circuitOpenUntilTicks, DateTime.UtcNow.Add(CircuitCooldown).Ticks);
            _logger.LogWarning(
                "Mídia: {N} falhas seguidas ({Reason}); pausando o poller por {Secs}s " +
                "(comandos do usuário continuam passando). " +
                "Normalmente é o broker de mídia do Windows travado, não o DroidDeck — " +
                "confirme com um cliente WinRT qualquer antes de procurar bug aqui.",
                _consecutiveFailures, reason, CircuitCooldown.TotalSeconds);
            _consecutiveFailures = 0;
        }

        public Task<List<MediaSessionInfo>> GetAllSessionsAsync(bool includeThumbnails = true) =>
            RunAsync(async manager =>
            {
                var sw = Stopwatch.StartNew();
                var sessions = manager.GetSessions();
                _logger.LogDebug("[diag] GetSessions() -> {N} sessoes em {Ms}ms", sessions.Count, sw.ElapsedMilliseconds);

                var result = new List<MediaSessionInfo>();
                foreach (var session in sessions)
                {
                    var swS = Stopwatch.StartNew();
                    var info = await GetSessionInfoAsync(session, includeThumbnails).ConfigureAwait(false);
                    _logger.LogDebug("[diag] sessao '{Id}' total {Ms}ms", session.SourceAppUserModelId, swS.ElapsedMilliseconds);
                    if (info != null) result.Add(info);
                }
                return result;
            },
            new List<MediaSessionInfo>(),
            nameof(GetAllSessionsAsync),
            Origin.Interactive,
            includeThumbnails ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(4));

        public Task<MediaSessionInfo?> GetSessionByIdAsync(string sessionId) =>
            RunAsync<MediaSessionInfo?>(async manager =>
            {
                var matches = manager.GetSessions()
                    .Where(x => x.SourceAppUserModelId == sessionId)
                    .ToList();
                var session = matches.FirstOrDefault(IsPlaying) ?? matches.FirstOrDefault();
                return session != null ? await GetSessionInfoAsync(session, true).ConfigureAwait(false) : null;
            },
            null,
            nameof(GetSessionByIdAsync),
            Origin.Interactive);

        private GlobalSystemMediaTransportControlsSession? ResolveSession(
            GlobalSystemMediaTransportControlsSessionManager manager, string? sessionId)
        {
            var sessions = manager.GetSessions();

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var matches = sessions.Where(s => s.SourceAppUserModelId == sessionId).ToList();
                if (matches.Count > 0)
                    return matches.FirstOrDefault(IsPlaying) ?? matches[0];

                _logger.LogWarning(
                    "Mídia: sessão '{SessionId}' não existe mais; usando a sessão ativa do sistema.", sessionId);
            }

            return manager.GetCurrentSession()
                ?? sessions.FirstOrDefault(IsPlaying)
                ?? sessions.FirstOrDefault();
        }

        private static double LivePosition(
            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline,
            GlobalSystemMediaTransportControlsSessionPlaybackInfo playback)
        {
            var position = timeline.Position.TotalSeconds;
            if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
                timeline.LastUpdatedTime.Year > 2000)
            {
                var elapsed = (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds;
                if (elapsed > 0) position += elapsed * (playback.PlaybackRate ?? 1.0);
            }
            var end = timeline.EndTime.TotalSeconds;
            return end > 0 ? Math.Min(position, end) : position;
        }

        private const int RPC_E_WRONG_THREAD = unchecked((int)0x8001010E);

        private static string SafeId(GlobalSystemMediaTransportControlsSession s)
        {
            try { return s.SourceAppUserModelId; } catch { return "?"; }
        }

        private static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
        {
            try
            {
                return s.GetPlaybackInfo().PlaybackStatus ==
                       GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch
            {
                return false;
            }
        }

        private async Task<MediaSessionInfo?> GetSessionInfoAsync(
            GlobalSystemMediaTransportControlsSession session, bool includeThumbnail)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var mediaProperties = await session.TryGetMediaPropertiesAsync();
                _logger.LogDebug("[diag]   TryGetMediaPropertiesAsync {Ms}ms", sw.ElapsedMilliseconds);

                sw.Restart();
                var playbackInfo = session.GetPlaybackInfo();
                var timelineProperties = session.GetTimelineProperties();
                _logger.LogDebug("[diag]   GetPlaybackInfo+Timeline {Ms}ms", sw.ElapsedMilliseconds);

                string? thumbnailBase64 = null;
                if (includeThumbnail && mediaProperties.Thumbnail != null)
                {
                    var streamRef = mediaProperties.Thumbnail;
                    sw.Restart();
                    using var stream = await streamRef.OpenReadAsync();
                    _logger.LogDebug("[diag]   thumb OpenReadAsync {Ms}ms", sw.ElapsedMilliseconds);

                    sw.Restart();
                    using var reader = new DataReader(stream.GetInputStreamAt(0));
                    var bytes = new byte[stream.Size];
                    await reader.LoadAsync((uint)stream.Size);
                    reader.ReadBytes(bytes);
                    thumbnailBase64 = "data:image/png;base64," + Convert.ToBase64String(bytes);
                    _logger.LogDebug("[diag]   thumb LoadAsync+Read {Bytes}B em {Ms}ms", bytes.Length, sw.ElapsedMilliseconds);
                }

                return new MediaSessionInfo
                {
                    Id = session.SourceAppUserModelId,
                    Title = mediaProperties.Title,
                    Artist = mediaProperties.Artist,
                    AlbumTitle = mediaProperties.AlbumTitle,
                    ThumbnailBase64 = thumbnailBase64,
                    PlaybackStatus = playbackInfo.PlaybackStatus.ToString(),
                    CanPlayPause = playbackInfo.Controls.IsPlayPauseToggleEnabled,
                    CanGoNext = playbackInfo.Controls.IsNextEnabled,
                    CanGoPrevious = playbackInfo.Controls.IsPreviousEnabled,
                    Position = LivePosition(timelineProperties, playbackInfo),
                    Duration = timelineProperties.EndTime.TotalSeconds
                };
            }
            catch (COMException ex) when (ex.HResult == RPC_E_WRONG_THREAD)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mídia: falha lendo a sessão '{Id}' (thread {Thread}, {Apt})",
                    SafeId(session), Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState());
                return null;
            }
        }

        public Task<MediaSessionInfo?> GetNowPlayingAsync(bool includeThumbnail) =>
            RunAsync<MediaSessionInfo?>(async manager =>
            {
                var session = ResolveSession(manager, null);
                return session != null ? await GetSessionInfoAsync(session, includeThumbnail).ConfigureAwait(false) : null;
            },
            null,
            nameof(GetNowPlayingAsync),
            includeThumbnail ? Origin.Interactive : Origin.Background,
            includeThumbnail ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(3));

        public Task<bool> SeekAsync(double seconds) =>
            RunAsync(async manager =>
            {
                var session = ResolveSession(manager, null);
                if (session == null) return false;
                var ticks = TimeSpan.FromSeconds(Math.Max(0, seconds)).Ticks;
                return await session.TryChangePlaybackPositionAsync(ticks);
            },
            false,
            nameof(SeekAsync),
            Origin.Interactive);

        public Task<bool> IsAnythingPlayingAsync() =>
            RunAsync(manager =>
            {
                var current = manager.GetCurrentSession();
                if (current != null && IsPlaying(current))
                    return Task.FromResult(true);

                foreach (var s in manager.GetSessions())
                {
                    if (IsPlaying(s)) return Task.FromResult(true);
                }
                return Task.FromResult(false);
            },
            false,
            nameof(IsAnythingPlayingAsync),
            Origin.Background,
            TimeSpan.FromSeconds(3));

        public Task<MediaCommandResult> SendCommandAsync(string? sessionId, string command) =>
            RunAsync(async manager =>
            {
                var session = ResolveSession(manager, sessionId);
                if (session == null)
                {
                    _logger.LogWarning("Mídia: comando '{Command}' ignorado — nenhuma sessão de mídia ativa.", command);
                    return new MediaCommandResult { Success = false };
                }

                var resolvedId = session.SourceAppUserModelId;

                switch (command.ToLower())
                {
                    case "play":
                        await session.TryPlayAsync();
                        break;
                    case "pause":
                        await session.TryPauseAsync();
                        break;
                    case "playpause":
                    case "toggle":
                        await session.TryTogglePlayPauseAsync();
                        break;
                    case "next":
                        await session.TrySkipNextAsync();
                        break;
                    case "previous":
                        await session.TrySkipPreviousAsync();
                        break;
                    case "stop":
                        await session.TryStopAsync();
                        break;
                    default:
                        _logger.LogWarning("Unknown command: {command}", command);
                        return new MediaCommandResult { Success = false, SessionId = resolvedId };
                }

                _logger.LogInformation("Sent command {command} to session {sessionId}", command, resolvedId);
                return new MediaCommandResult
                {
                    Success = true,
                    SessionId = resolvedId,
                    Playing = IsPlaying(session)
                };
            },
            new MediaCommandResult { Success = false },
            nameof(SendCommandAsync),
            Origin.Interactive);
    }

    public class MediaCommandResult
    {
        public bool Success { get; set; }

        public string? SessionId { get; set; }

        public bool? Playing { get; set; }
    }

    public class MediaSessionInfo
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? AlbumTitle { get; set; }
        public string? ThumbnailBase64 { get; set; }
        public string? PlaybackStatus { get; set; }
        public bool CanPlayPause { get; set; }
        public bool CanGoNext { get; set; }
        public bool CanGoPrevious { get; set; }
        public double Position { get; set; }
        public double Duration { get; set; }
    }
}
