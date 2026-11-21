import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/home/home_controller.dart';
import 'package:droiddeck/src/kz/kz_audio.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/foundation.dart';

/// Um aplicativo da biblioteca do PC (o caminho nunca chega ao celular).
class KzApp {
  KzApp({
    required this.id,
    required this.name,
    required this.group,
    required this.favorite,
    this.icon,
    this.path,
  });

  final String id;
  final String name;
  final String group;
  final bool favorite;
  final Uint8List? icon;
  final String? path; // só vem para o configurador no próprio PC

  factory KzApp.fromJson(Map<String, dynamic> j) {
    Uint8List? icon;
    final b64 = j['iconBase64'];
    if (b64 is String && b64.isNotEmpty) {
      try {
        icon = base64Decode(b64);
      } catch (_) {}
    }
    return KzApp(
      id: '${j['id']}',
      name: '${j['name'] ?? ''}',
      group: '${j['group'] ?? ''}',
      favorite: j['favorite'] == true,
      icon: icon,
      path: j['path'] as String?,
    );
  }
}

/// Estado compartilhado das telas do KZ Companion: biblioteca de apps, histórico dos
/// sensores (gráficos) e o mixer/mídia já existentes do DroidDeck.
class KzController {
  KzController({DroidDeckClient? client, SignalRService? signalR})
      : client = client ?? Injector.get<DroidDeckClient>(),
        signalR = signalR ?? Injector.get<SignalRService>() {
    home = HomeController(client: this.client, signalR: this.signalR);
    audio = AudioStore(client: this.client, signalR: this.signalR);
    _disposers.add(effect(() {
      final s = this.signalR.systemStats.value;
      untracked(() => _push(s));
    }));
    _disposers.add(effect(() {
      this.signalR.appsChanged.value;
      untracked(refreshApps);
    }));
    var wasConnected = false;
    _disposers.add(effect(() {
      final connected = this.signalR.connectionStatus.value == 'Connected';
      if (connected && !wasConnected) {
        untracked(() {
          refreshPcInfo();
          refreshApps();
          home.fetchOutputs();
          home.fetchMediaSessions();
        });
      }
      wasConnected = connected;
    }));
    home.startPolling();
    refreshApps();
    _appsTimer = Timer.periodic(const Duration(seconds: 60), (_) => refreshApps());
  }

  static const historyLength = 60;

  final DroidDeckClient client;
  final SignalRService signalR;
  late final HomeController home;
  late final AudioStore audio;

  final apps = signal<List<KzApp>>([]);
  final appsError = signal<String?>(null);

  final cpuHistory = signal<List<double>>([]);
  final gpuHistory = signal<List<double>>([]);
  final ramHistory = signal<List<double>>([]);
  final cpuTempHistory = signal<List<double>>([]);
  final gpuTempHistory = signal<List<double>>([]);

  final _disposers = <void Function()>[];
  Timer? _appsTimer;

  bool get connected => signalR.connectionStatus.value == 'Connected';

  List<double> _append(List<double> list, double v) {
    final next = [...list, v];
    return next.length > historyLength ? next.sublist(next.length - historyLength) : next;
  }

  void _push(SystemStats s) {
    // Sem dado novo (PC desconectado) não enche o gráfico com zeros.
    if (!connected) return;
    cpuHistory.value = _append(cpuHistory.value, s.cpuUsage);
    gpuHistory.value = _append(gpuHistory.value, s.gpuUsage);
    ramHistory.value = _append(ramHistory.value, s.ramUsedPercent);
    if (s.cpuTemp != null) cpuTempHistory.value = _append(cpuTempHistory.value, s.cpuTemp!);
    if (s.gpuTemp != null) gpuTempHistory.value = _append(gpuTempHistory.value, s.gpuTemp!);
  }

  /// Nome do PC e versao do Windows para o cabecalho.
  Future<void> refreshPcInfo() async {
    try {
      final p = await client.getPing();
      signalR.pcName.value = '${p['name'] ?? ''}';
      signalR.pcOs.value = '${p['os'] ?? ''}';
    } catch (_) {}
  }

  Future<void> refreshApps() async {
    try {
      final raw = await client.getApps();
      apps.value = raw.map(KzApp.fromJson).toList();
      appsError.value = null;
    } catch (e) {
      appsError.value = e.toString();
      debugPrint('KZ apps: $e');
    }
  }

  Future<String?> launch(KzApp app) async {
    try {
      await client.launchApp(app.id);
      return null;
    } catch (e) {
      return friendlyError(e);
    }
  }

  /// Sessão de mídia principal (a que está tocando, senão a primeira).
  MediaSession? get mediaSession {
    final all = home.mediaSessions.value.values.toList();
    if (all.isEmpty) return null;
    return all.firstWhere((s) => s.playbackStatus == 'Playing', orElse: () => all.first);
  }

  /// Saída de áudio padrão do Windows (a primeira que o mixer lista).
  MixerEntity? get mainOutput {
    final o = home.outputs.value;
    return o.isEmpty ? null : o.first;
  }

  Future<void> media(String command) async {
    final s = mediaSession;
    if (s?.id == null) return;
    await home.sendMediaCommand(s!.id!, command);
  }

  void dispose() {
    for (final d in _disposers) {
      d();
    }
    _appsTimer?.cancel();
    audio.dispose();
    home.dispose();
  }
}
