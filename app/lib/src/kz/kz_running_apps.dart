import 'dart:async';
import 'dart:typed_data';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/services/signalr_service.dart';

class KzWindow {
  const KzWindow({required this.id, required this.title, required this.minimized, required this.foreground});
  final String id;
  final String title;
  final bool minimized;
  final bool foreground;

  factory KzWindow.fromJson(Map<String, dynamic> j) => KzWindow(
        id: '${j['id']}',
        title: '${j['title'] ?? ''}',
        minimized: j['minimized'] == true,
        foreground: j['foreground'] == true,
      );
}

class KzRunningApp {
  const KzRunningApp({
    required this.id,
    required this.name,
    required this.title,
    required this.foreground,
    required this.minimized,
    required this.elevated,
    required this.windows,
  });

  final String id;
  final String name;
  final String title;
  final bool foreground;
  final bool minimized;

  final bool elevated;
  final List<KzWindow> windows;

  String get subtitle {
    final t = title.trim();
    if (t.isEmpty || t.toLowerCase() == name.toLowerCase()) return '';
    return t;
  }

  factory KzRunningApp.fromJson(Map<String, dynamic> j) => KzRunningApp(
        id: '${j['id']}',
        name: '${j['name'] ?? j['processName'] ?? ''}',
        title: '${j['title'] ?? ''}',
        foreground: j['foreground'] == true,
        minimized: j['minimized'] == true,
        elevated: j['elevated'] == true,
        windows: ((j['windows'] as List?) ?? const [])
            .whereType<Map>()
            .map((e) => KzWindow.fromJson(e.cast<String, dynamic>()))
            .toList(),
      );
}

class RunningAppResult {
  const RunningAppResult(this.message, {this.refresh = false});
  final String? message;
  final bool refresh;
}

class RunningAppsStore {
  RunningAppsStore({required this.client, required this.signalR}) {
    _disposers.add(effect(() {
      final push = signalR.runningAppsPush.value;
      if (push != null) untracked(() => _apply(push));
    }));
    var wasConnected = false;
    _disposers.add(effect(() {
      final connected = signalR.connectionStatus.value == 'Connected';
      if (connected && !wasConnected && _active) untracked(refresh);
      wasConnected = connected;
    }));
  }

  final DroidDeckClient client;
  final SignalRService signalR;

  final apps = signal<List<KzRunningApp>>([]);
  final loaded = signal<bool>(false);
  final error = signal<String?>(null);
  final icons = signal<Map<String, Uint8List>>({});

  final _disposers = <void Function()>[];
  final _iconRequested = <String>{};
  bool _active = false;

  bool get active => _active;

  void start() {
    if (_active) return;
    _active = true;
    signalR.watchRunningApps(true);
    refresh();
  }

  void stop() {
    if (!_active) return;
    _active = false;
    signalR.watchRunningApps(false);
  }

  Future<void> refresh() async {
    try {
      _apply(await client.getRunningApps());
      error.value = null;
    } catch (e) {
      error.value = friendlyError(e);
    }
  }

  void _apply(List<Map<String, dynamic>> raw) {
    apps.value = raw.map(KzRunningApp.fromJson).toList();
    loaded.value = true;
    for (final a in apps.value) {
      ensureIcon(a.id);
    }
  }

  Future<void> ensureIcon(String id) async {
    if (icons.value.containsKey(id) || !_iconRequested.add(id)) return;
    try {
      final bytes = await client.getRunningAppIcon(id);
      if (bytes.isNotEmpty) icons.value = {...icons.value, id: Uint8List.fromList(bytes)};
    } catch (_) {
    }
  }

  Future<RunningAppResult> focus(KzRunningApp app, {KzWindow? window}) =>
      _run(app, 'focus', windowId: window?.id);

  Future<RunningAppResult> minimize(KzRunningApp app, {KzWindow? window}) =>
      _run(app, 'minimize', windowId: window?.id);

  Future<RunningAppResult> close(KzRunningApp app, {KzWindow? window}) =>
      _run(app, 'close', windowId: window?.id, confirm: true);

  Future<RunningAppResult> _run(KzRunningApp app, String action, {String? windowId, bool confirm = false}) async {
    try {
      final r = await client.runningAppAction(app.id, action, windowId: windowId, confirm: confirm);
      if (action == 'close') {
        refresh();
        return r['closed'] == false
            ? RunningAppResult('${app.name} is still open. It may be asking to save your work.')
            : const RunningAppResult(null);
      }
      return const RunningAppResult(null);
    } on DioException catch (e) {
      final code = (e.response?.data is Map) ? (e.response!.data as Map)['error'] : null;
      switch (code) {
        case 'app_gone':
        case 'window_gone':
          refresh();
          return RunningAppResult('${app.name} was already closed.', refresh: true);
        case 'elevated':
          return RunningAppResult('${app.name} runs as administrator. Control it on the PC.');
        case 'focus_denied':
          return RunningAppResult('Windows would not bring ${app.name} to the front. Try again.');
      }
      return RunningAppResult(friendlyError(e));
    } catch (e) {
      return RunningAppResult(friendlyError(e));
    }
  }

  void dispose() {
    stop();
    for (final d in _disposers) {
      d();
    }
  }
}
