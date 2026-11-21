import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/foundation.dart';

class AudioSession {
  const AudioSession({
    required this.id,
    required this.name,
    required this.volume,
    required this.muted,
    required this.active,
    required this.system,
  });

  final String id;
  final String name;
  final int volume;
  final bool muted;
  final bool active;
  final bool system;

  factory AudioSession.fromJson(Map<String, dynamic> j) => AudioSession(
        id: '${j['id']}',
        name: '${j['name'] ?? ''}',
        volume: ((j['volume'] as num?) ?? 0).toInt().clamp(0, 100),
        muted: j['muted'] == true,
        active: j['active'] == true,
        system: j['system'] == true,
      );
}

class AudioState {
  const AudioState({
    this.outputName = '',
    this.volume,
    this.muted = false,
    this.inputMuted,
    this.deafened = false,
    this.sessions = const [],
  });

  final String outputName;
  final int? volume; // null = o PC nao tem dispositivo de saida
  final bool muted;
  final bool? inputMuted; // null = sem microfone
  final bool deafened;
  final List<AudioSession> sessions;

  factory AudioState.fromJson(Map<String, dynamic> j) => AudioState(
        outputName: '${j['outputName'] ?? ''}',
        volume: (j['volume'] as num?)?.toInt().clamp(0, 100),
        muted: j['muted'] == true,
        inputMuted: j['inputMuted'] as bool?,
        deafened: j['deafened'] == true,
        sessions: ((j['sessions'] as List?) ?? const [])
            .whereType<Map>()
            .map((e) => AudioSession.fromJson(e.cast<String, dynamic>()))
            .toList(),
      );
}

/// Espelho do audio do Windows (que e a fonte da verdade). Chega por push a cada mudanca no PC;
/// comandos do celular so aplicam no PC e o estado confirmado volta pelo mesmo push. Itens que o
/// usuario esta arrastando (ou acabou de soltar) ignoram o push por um instante, sem loop.
class AudioStore {
  AudioStore() {
    _disposers.add(effect(() {
      final push = signalR.audioPush.value;
      if (push != null) untracked(() => _apply(push, fromPush: true));
    }));
    var wasOnline = false;
    _disposers.add(effect(() {
      final online = signalR.linkState.value == 'online';
      if (online && !wasOnline) untracked(refresh);
      wasOnline = online;
    }));
  }

  final client = Injector.get<DroidDeckClient>();
  final signalR = Injector.get<SignalRService>();

  final state = signal<AudioState?>(null);
  final icons = signal<Map<String, Uint8List>>({});

  final _disposers = <void Function()>[];
  final _lockUntil = <String, DateTime>{}; // "master" ou id da sessao
  final _lastSend = <String, DateTime>{};
  Timer? _refreshDebounce;

  bool get online => signalR.linkState.value == 'online';

  /// Busca o estado completo (com icones). Chamado ao conectar, ao voltar ao app e se aparecer sessao desconhecida.
  Future<void> refresh() async {
    try {
      final j = await client.getAudio();
      final newIcons = {...icons.value};
      for (final s in (j['sessions'] as List? ?? const []).whereType<Map>()) {
        final b64 = s['icon'];
        if (b64 is String && b64.isNotEmpty) {
          try {
            newIcons['${s['id']}'] = base64Decode(b64);
          } catch (_) {}
        }
      }
      icons.value = newIcons;
      _apply(j, fromPush: false);
    } catch (e) {
      debugPrint('KZ audio: $e');
    }
  }

  void _apply(Map<String, dynamic> j, {required bool fromPush}) {
    var next = AudioState.fromJson(j);
    final prev = state.value;
    final now = DateTime.now();

    // Sessoes que o usuario esta mexendo agora mantem o valor local ate o PC confirmar.
    if (prev != null) {
      next = AudioState(
        outputName: next.outputName,
        volume: _locked('master', now) ? prev.volume : next.volume,
        muted: _locked('master', now) ? prev.muted : next.muted,
        inputMuted: next.inputMuted,
        deafened: next.deafened,
        sessions: [
          for (final s in next.sessions)
            if (_locked(s.id, now))
              prev.sessions.firstWhere((p) => p.id == s.id, orElse: () => s)
            else
              s
        ],
      );
    }
    state.value = next;

    // Sessao nova sem icone: pede o estado completo uma vez (o push nao leva icones).
    if (fromPush && next.sessions.any((s) => !s.system && !icons.value.containsKey(s.id))) {
      _refreshDebounce?.cancel();
      _refreshDebounce = Timer(const Duration(milliseconds: 400), refresh);
    }
  }

  bool _locked(String key, DateTime now) {
    final t = _lockUntil[key];
    return t != null && now.isBefore(t);
  }

  void _lock(String key, [int ms = 900]) =>
      _lockUntil[key] = DateTime.now().add(Duration(milliseconds: ms));

  bool _throttled(String key) {
    final now = DateTime.now();
    final last = _lastSend[key];
    if (last != null && now.difference(last) < const Duration(milliseconds: 120)) return true;
    _lastSend[key] = now;
    return false;
  }

  // ---- comandos (otimistas: a tela responde ja; o PC confirma pelo push) ----

  void setMasterVolume(int v, {bool commit = false}) {
    final s = state.value;
    if (s == null) return;
    _lock('master');
    state.value = AudioState(
        outputName: s.outputName,
        volume: v,
        muted: s.muted,
        inputMuted: s.inputMuted,
        deafened: s.deafened,
        sessions: s.sessions);
    if (commit || !_throttled('master')) _send(() => client.setAudioMaster(volume: v));
  }

  void toggleMasterMute() {
    final s = state.value;
    if (s == null) return;
    _lock('master', 600);
    final m = !s.muted;
    state.value = AudioState(
        outputName: s.outputName,
        volume: s.volume,
        muted: m,
        inputMuted: s.inputMuted,
        deafened: s.deafened && m,
        sessions: s.sessions);
    _send(() => client.setAudioMaster(mute: m));
  }

  void toggleDeafen() {
    final s = state.value;
    if (s == null) return;
    _send(() => client.setDeafen(!s.deafened));
  }

  void setSessionVolume(String id, int v, {bool commit = false}) {
    _patch(id, (s) => AudioSession(id: s.id, name: s.name, volume: v, muted: s.muted, active: s.active, system: s.system));
    _lock(id);
    if (commit || !_throttled(id)) _send(() => client.setAudioSession(id, volume: v));
  }

  void toggleSessionMute(String id) {
    final cur = state.value?.sessions.where((s) => s.id == id).firstOrNull;
    if (cur == null) return;
    final m = !cur.muted;
    _patch(id, (s) => AudioSession(id: s.id, name: s.name, volume: s.volume, muted: m, active: s.active, system: s.system));
    _lock(id, 600);
    _send(() => client.setAudioSession(id, mute: m));
  }

  void _patch(String id, AudioSession Function(AudioSession) f) {
    final s = state.value;
    if (s == null) return;
    state.value = AudioState(
      outputName: s.outputName,
      volume: s.volume,
      muted: s.muted,
      inputMuted: s.inputMuted,
      deafened: s.deafened,
      sessions: [for (final x in s.sessions) x.id == id ? f(x) : x],
    );
  }

  Future<void> _send(Future<void> Function() call) async {
    try {
      await call();
    } on DioException catch (e) {
      // 404 = a sessao fechou enquanto o dedo estava no slider: so atualiza a lista.
      if (e.response?.statusCode == 404) {
        refresh();
      } else {
        debugPrint('KZ audio command: $e');
      }
    } catch (e) {
      debugPrint('KZ audio command: $e');
    }
  }

  void dispose() {
    for (final d in _disposers) {
      d();
    }
    _refreshDebounce?.cancel();
  }
}
