using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DroidDeck.Hubs;
using DroidDeck.Lib;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DroidDeck.Services
{
    public sealed class AudioSessionDto
    {
        public string Id { get; set; } = "";
        public int Pid { get; set; }
        public string Name { get; set; } = "";
        public bool System { get; set; }
        public bool Active { get; set; }
        public int Volume { get; set; }
        public bool Muted { get; set; }
        public string? Icon { get; set; }
    }

    public sealed class AudioStateDto
    {
        public string? OutputId { get; set; }
        public string OutputName { get; set; } = "";
        public int? Volume { get; set; }      // null = sem dispositivo de saida
        public bool Muted { get; set; }
        public string? InputId { get; set; }
        public string InputName { get; set; } = "";
        public bool? InputMuted { get; set; } // null = sem microfone
        public bool Deafened { get; set; }
        public List<AudioSessionDto> Sessions { get; set; } = new();
    }

    /// <summary>
    /// Estado de audio do Windows (autoritativo): volume e mudo do dispositivo de saida padrao,
    /// mudo do microfone padrao e as sessoes de audio por aplicativo (volume proprio de cada
    /// sessao, SEM multiplicar pelo mestre). O Windows avisa por eventos (volume, sessao nova,
    /// troca do dispositivo padrao) e so entao o estado e enviado ao celular; um repasse a cada
    /// 5 s cobre evento perdido. Objetos COM ficam vivos so enquanto registrados.
    /// </summary>
    public sealed class AudioMixerService : BackgroundService, IMMNotificationClient
    {
        private readonly IHubContext<DeckHub> _hub;
        private readonly object _gate = new();
        private MMDeviceEnumerator? _enumerator;
        private MMDevice? _render;
        private MMDevice? _capture;
        private readonly Dictionary<string, Tracked> _sessions = new(); // por id da instancia da sessao
        private readonly SessionEvents _events;
        private readonly AudioEndpointVolumeNotificationDelegate _renderCb;
        private readonly AudioEndpointVolumeNotificationDelegate _captureCb;
        private System.Threading.Timer? _debounce;
        private string _lastJson = "";

        // Deafen: guarda o mudo anterior por dispositivo (id), para devolver exatamente como estava.
        private bool _deafen;
        private string? _deafOutId, _deafInId;
        private bool _deafOutPrev, _deafInPrev;

        private sealed class Tracked
        {
            public AudioSessionControl Control = null!;
            public string ShortId = "";
        }

        private sealed record ProcInfo(string Name, string? Icon);
        private readonly Dictionary<int, ProcInfo> _procCache = new();

        public AudioMixerService(IHubContext<DeckHub> hub)
        {
            _hub = hub;
            _events = new SessionEvents(MarkDirty);
            _renderCb = _ => MarkDirty();
            _captureCb = _ => MarkDirty();
        }

        // ---- ciclo de vida ----

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!OperatingSystem.IsWindows()) return;
            // Em thread do pool (MTA): o host sobe servicos na thread STA da janela, e um objeto COM
            // criado la nao pode ser usado depois pelas threads das requisicoes (E_NOINTERFACE).
            await Task.Run(() =>
            {
                try
                {
                    lock (_gate)
                    {
                        _enumerator = new MMDeviceEnumerator();
                        _enumerator.RegisterEndpointNotificationCallback(this);
                        Rebind();
                    }
                }
                catch (Exception ex) { Log.Warning($"Audio: inicializacao falhou: {ex.Message}"); }
            }, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(5000, stoppingToken); } catch (OperationCanceledException) { break; }
                if (DeckHub.ConnectedCount > 0) { MarkDirty(); }
            }
        }

        public override void Dispose()
        {
            lock (_gate)
            {
                Unbind();
                try { _enumerator?.UnregisterEndpointNotificationCallback(this); } catch { }
                _enumerator?.Dispose();
                _enumerator = null;
            }
            base.Dispose();
        }

        /// <summary>(Re)abre os dispositivos padrao e assina os eventos. Chamar com _gate.</summary>
        private void Rebind()
        {
            Unbind();
            if (_enumerator == null) return;
            try { _render = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); } catch (Exception ex) { _render = null; Log.Warning($"Audio: sem saida padrao: {ex.Message}"); }
            try { _capture = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); } catch { _capture = null; }
            try { if (_render != null) _render.AudioEndpointVolume.OnVolumeNotification += _renderCb; } catch { }
            try { if (_capture != null) _capture.AudioEndpointVolume.OnVolumeNotification += _captureCb; } catch { }
            try
            {
                if (_render != null) _render.AudioSessionManager.OnSessionCreated += OnSessionCreated;
            }
            catch { }
            SyncSessions();
        }

        private void Unbind()
        {
            foreach (var t in _sessions.Values) DropTracked(t);
            _sessions.Clear();
            try { if (_render != null) _render.AudioEndpointVolume.OnVolumeNotification -= _renderCb; } catch { }
            try { if (_capture != null) _capture.AudioEndpointVolume.OnVolumeNotification -= _captureCb; } catch { }
            try { if (_render != null) _render.AudioSessionManager.OnSessionCreated -= OnSessionCreated; } catch { }
            try { _render?.Dispose(); } catch { }
            try { _capture?.Dispose(); } catch { }
            _render = null;
            _capture = null;
        }

        private void DropTracked(Tracked t)
        {
            try { t.Control.UnRegisterEventClient(_events); } catch { }
            try { t.Control.Dispose(); } catch { }
        }

        private void OnSessionCreated(object sender, IAudioSessionControl newSession) => MarkDirty(rescan: true);

        /// <summary>Descobre sessoes novas e esquece as expiradas. Chamar com _gate.</summary>
        private void SyncSessions()
        {
            if (_render == null) return;
            var live = new HashSet<string>();
            try
            {
                var mgr = _render.AudioSessionManager;
                mgr.RefreshSessions();
                var col = mgr.Sessions;
                for (int i = 0; i < col.Count; i++)
                {
                    var c = col[i];
                    string key;
                    try { key = c.GetSessionInstanceIdentifier; } catch { c.Dispose(); continue; }
                    live.Add(key);
                    if (_sessions.ContainsKey(key)) { c.Dispose(); continue; } // ja acompanhada
                    try { c.RegisterEventClient(_events); } catch { }
                    _sessions[key] = new Tracked { Control = c, ShortId = ShortHash(key) };
                }
            }
            catch (Exception ex) { Log.Warning($"Audio: varredura de sessoes falhou: {ex.Message}"); }

            foreach (var key in _sessions.Keys.Where(k => !live.Contains(k)).ToList())
            {
                DropTracked(_sessions[key]);
                _sessions.Remove(key);
            }
        }

        private static string ShortHash(string s) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..12].ToLowerInvariant();

        // ---- eventos -> envio ao celular (debounce de 80 ms) ----

        private bool _rescan;

        private readonly object _timerGate = new();

        // NAO usar _gate aqui: o Windows chama os eventos de volume de forma sincrona, de outra
        // thread, enquanto quem esta mudando o volume ainda segura _gate (deadlock garantido).
        private void MarkDirty(bool rescan = false)
        {
            if (rescan) _rescan = true;
            lock (_timerGate)
            {
                _debounce ??= new System.Threading.Timer(_ => _ = PushAsync(), null, Timeout.Infinite, Timeout.Infinite);
                _debounce.Change(80, Timeout.Infinite);
            }
        }

        private async Task PushAsync()
        {
            AudioStateDto state;
            lock (_gate)
            {
                if (_rescan) { _rescan = false; SyncSessions(); }
                else SyncSessions();
                state = Snapshot(withIcons: false);
            }
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            if (json == _lastJson) return; // nada mudou: nao gasta rede
            _lastJson = json;
            if (DeckHub.ConnectedCount <= 0) return;
            try { await _hub.Clients.All.SendAsync("ReceiveAudioState", state); }
            catch (Exception ex) { Log.Warning($"Audio: envio falhou: {ex.Message}"); }
        }

        // ---- snapshot ----

        public AudioStateDto GetState(bool withIcons)
        {
            lock (_gate)
            {
                SyncSessions();
                return Snapshot(withIcons);
            }
        }

        private AudioStateDto Snapshot(bool withIcons)
        {
            var s = new AudioStateDto();
            try
            {
                if (_render != null)
                {
                    s.OutputId = _render.ID;
                    s.OutputName = CleanName(_render.FriendlyName);
                    s.Volume = (int)Math.Round(_render.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                    s.Muted = _render.AudioEndpointVolume.Mute;
                }
            }
            catch (Exception ex) { s.Volume = null; Log.Warning($"Audio: leitura do mestre falhou: {ex.Message}"); }
            try
            {
                if (_capture != null)
                {
                    s.InputId = _capture.ID;
                    s.InputName = CleanName(_capture.FriendlyName);
                    s.InputMuted = _capture.AudioEndpointVolume.Mute;
                }
            }
            catch { s.InputMuted = null; }

            // Deafen so vale enquanto os dois continuam mudos nos mesmos dispositivos; se alguem
            // desmutou por fora (ou o padrao trocou), o estado e descartado sem mexer em nada.
            if (_deafen)
            {
                var still = s.OutputId == _deafOutId && s.Muted && (_deafInId == null || (s.InputId == _deafInId && s.InputMuted == true));
                if (!still) _deafen = false;
            }
            s.Deafened = _deafen;

            foreach (var t in _sessions.Values)
            {
                try
                {
                    var c = t.Control;
                    if (c.State == AudioSessionState.AudioSessionStateExpired) continue;
                    var dto = new AudioSessionDto
                    {
                        Id = t.ShortId,
                        Active = c.State == AudioSessionState.AudioSessionStateActive,
                        Volume = (int)Math.Round(c.SimpleAudioVolume.Volume * 100),
                        Muted = c.SimpleAudioVolume.Mute,
                    };
                    if (c.IsSystemSoundsSession)
                    {
                        dto.System = true;
                        dto.Name = "System Sounds";
                    }
                    else
                    {
                        var pid = (int)c.GetProcessID;
                        dto.Pid = pid;
                        var info = ProcessInfoFor(pid, c);
                        dto.Name = info.Name;
                        if (withIcons) dto.Icon = info.Icon;
                    }
                    s.Sessions.Add(dto);
                }
                catch { /* sessao morreu no meio da leitura */ }
            }

            // Mesmo nome em varias sessoes: numera (cada uma continua independente).
            foreach (var g in s.Sessions.GroupBy(x => x.Name).Where(g => g.Count() > 1))
            {
                var n = 1;
                foreach (var x in g) x.Name = $"{x.Name} ({n++})";
            }
            s.Sessions = s.Sessions.OrderByDescending(x => x.System ? 0 : x.Active ? 2 : 1)
                                   .ThenBy(x => x.System ? "" : x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            // Ordem estavel: Sons do sistema por ultimo na lista visual.
            s.Sessions = s.Sessions.Where(x => !x.System).Concat(s.Sessions.Where(x => x.System)).ToList();
            return s;
        }

        private static string CleanName(string friendly)
        {
            var i = friendly.IndexOf('(');
            return i > 0 ? friendly[..i].Trim() : friendly.Trim();
        }

        // ---- nome e icone do processo (cache por PID) ----

        private ProcInfo ProcessInfoFor(int pid, AudioSessionControl c)
        {
            if (_procCache.TryGetValue(pid, out var cached)) return cached;
            string name = "";
            string? icon = null;
            string? path = QueryProcessPath(pid);
            try
            {
                var display = c.DisplayName;
                if (!string.IsNullOrWhiteSpace(display) && !display.StartsWith("@", StringComparison.Ordinal)) name = display.Trim();
            }
            catch { }
            if (path != null)
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    if (name.Length == 0) name = (vi.FileDescription ?? "").Trim();
                    if (name.Length == 0) name = (vi.ProductName ?? "").Trim();
                }
                catch { }
                if (name.Length == 0) name = System.IO.Path.GetFileNameWithoutExtension(path);
                var png = AppLibrary.ExtractIconPng(path);
                if (png != null) icon = Convert.ToBase64String(png);
            }
            if (name.Length == 0)
            {
                try { using var p = Process.GetProcessById(pid); name = p.ProcessName; } catch { name = $"Process {pid}"; }
            }
            name = name.Replace("Â®", "").Replace("®", "").Replace("™", "").Trim();
            var info = new ProcInfo(name, icon);
            if (_procCache.Count > 200) _procCache.Clear();
            _procCache[pid] = info;
            return info;
        }

        private static string? QueryProcessPath(int pid)
        {
            var h = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        // ---- comandos (validados; resolvem a sessao AO VIVO pelo id) ----

        public (bool Ok, string? Error) SetMaster(int? volume, bool? mute)
        {
            if (volume is < 0 or > 100) return (false, "Volume must be 0-100.");
            lock (_gate)
            {
                if (_render == null) return (false, "No output device.");
                try
                {
                    var ev = _render.AudioEndpointVolume;
                    if (volume != null) ev.MasterVolumeLevelScalar = volume.Value / 100f;
                    if (mute != null) ev.Mute = mute.Value;
                }
                catch (Exception ex) { return (false, ex.Message); }
            }
            MarkDirty();
            return (true, null);
        }

        public (bool Ok, string? Error, bool NotFound) SetSession(string id, int? volume, bool? mute)
        {
            if (volume is < 0 or > 100) return (false, "Volume must be 0-100.", false);
            lock (_gate)
            {
                SyncSessions();
                var t = _sessions.Values.FirstOrDefault(x => x.ShortId == id);
                if (t == null) return (false, "Unknown or expired session.", true);
                try
                {
                    if (t.Control.State == AudioSessionState.AudioSessionStateExpired) return (false, "Session expired.", true);
                    var v = t.Control.SimpleAudioVolume;
                    if (volume != null) v.Volume = volume.Value / 100f;
                    if (mute != null) v.Mute = mute.Value;
                }
                catch (Exception ex) { return (false, ex.Message, false); }
            }
            MarkDirty();
            return (true, null, false);
        }

        /// <summary>Deafen do sistema: muta saida e microfone padrao e devolve o mudo ANTERIOR de cada um ao desligar.</summary>
        public (bool Ok, string? Error) SetDeafen(bool enable)
        {
            lock (_gate)
            {
                try
                {
                    if (enable)
                    {
                        if (_deafen) return (true, null);
                        if (_render == null) return (false, "No output device.");
                        _deafOutId = _render.ID;
                        _deafOutPrev = _render.AudioEndpointVolume.Mute;
                        _deafInId = _capture?.ID;
                        _deafInPrev = _capture?.AudioEndpointVolume.Mute ?? false;
                        _render.AudioEndpointVolume.Mute = true;
                        if (_capture != null) _capture.AudioEndpointVolume.Mute = true;
                        _deafen = true;
                    }
                    else
                    {
                        RestoreDeafen();
                    }
                }
                catch (Exception ex) { return (false, ex.Message); }
            }
            MarkDirty();
            return (true, null);
        }

        /// <summary>Devolve o mudo salvo aos dispositivos guardados (se ainda existirem). Chamar com _gate.</summary>
        private void RestoreDeafen()
        {
            if (!_deafen) return;
            _deafen = false;
            void Restore(string? id, bool prev)
            {
                if (id == null || _enumerator == null) return;
                try
                {
                    using var d = _enumerator.GetDevice(id);
                    if (d.State == DeviceState.Active) d.AudioEndpointVolume.Mute = prev;
                }
                catch { /* dispositivo sumiu: nada a restaurar */ }
            }
            Restore(_deafOutId, _deafOutPrev);
            Restore(_deafInId, _deafInPrev);
        }

        // ---- IMMNotificationClient: troca do dispositivo padrao ----

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (role == Role.Console && flow == DataFlow.Render) { /* Multimedia e Console costumam vir juntos */ }
            Task.Run(() =>
            {
                lock (_gate)
                {
                    // Trocou de dispositivo no meio do deafen: devolve o mudo dos antigos antes.
                    if (_deafen && (flow == DataFlow.Render ? defaultDeviceId != _deafOutId : defaultDeviceId != _deafInId))
                    {
                        try { RestoreDeafen(); } catch { }
                    }
                    Rebind();
                }
                MarkDirty();
            });
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Task.Run(() => { lock (_gate) Rebind(); MarkDirty(); });
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) => Task.Run(() => { lock (_gate) Rebind(); MarkDirty(); });
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        // ---- eventos de sessao ----

        private sealed class SessionEvents : IAudioSessionEventsHandler
        {
            private readonly Action<bool> _dirty;
            public SessionEvents(Action<bool> dirty) => _dirty = dirty;
            private void D() => _dirty(false);
            public void OnVolumeChanged(float volume, bool isMuted) => D();
            public void OnDisplayNameChanged(string displayName) => D();
            public void OnIconPathChanged(string iconPath) { }
            public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
            public void OnGroupingParamChanged(ref Guid groupingId) { }
            public void OnStateChanged(AudioSessionState state) => D();
            public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => D();
        }
    }
}
