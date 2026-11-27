import 'dart:async';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

class ClaudeSession {
  const ClaudeSession(this.id, this.project, this.status);
  final String id;
  final String project;
  final String status;

  factory ClaudeSession.fromJson(Map j) => ClaudeSession('${j['id']}', '${j['project']}', '${j['status']}');
}

class KzSessionWatch with WidgetsBindingObserver {
  KzSessionWatch(this.c, {void Function()? vibrate}) : _vibrate = vibrate ?? HapticFeedback.vibrate;

  static const every = Duration(seconds: 4);

  final KzController c;
  final void Function() _vibrate;

  final sessions = signal<List<ClaudeSession>>([]);
  final message = signal<String?>(null);

  final _last = <String, String>{};
  Timer? _timer;
  bool _loading = false;

  void start() {
    WidgetsBinding.instance.addObserver(this);
    _run();
  }

  void _run() {
    _timer?.cancel();
    _timer = Timer.periodic(every, (_) => load());
    load();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _run();
    } else if (state == AppLifecycleState.paused || state == AppLifecycleState.hidden) {
      _timer?.cancel();
    }
  }

  Future<void> load() async {
    if (_loading) return;
    _loading = true;
    try {
      final raw = await c.client.getAiSessions();
      if (_timer != null) apply(raw.map(ClaudeSession.fromJson).toList());
    } catch (_) {
    } finally {
      _loading = false;
    }
  }

  void apply(List<ClaudeSession> next) {
    String? alert;
    for (final s in next) {
      final before = _last[s.id];
      if (before == 'busy' && s.status == 'waiting') alert = '${s.project}: Claude is waiting for you';
      if (before == 'busy' && s.status == 'idle') alert = '${s.project}: Claude finished';
    }
    _last
      ..clear()
      ..addEntries([for (final s in next) MapEntry(s.id, s.status)]);
    sessions.value = next;
    if (alert != null) {
      message.value = alert;
      _vibrate();
    } else if (message.value != null && next.every((s) => s.status == 'busy')) {
      message.value = null;
    }
  }

  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _timer?.cancel();
    _timer = null;
  }
}

class KzSessionBanner extends StatelessWidget {
  const KzSessionBanner(this.watch, {super.key, required this.onOpen});
  final KzSessionWatch watch;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final text = watch.message.value;
      if (text == null) return const SizedBox.shrink();
      return Material(
        color: const Color(0xFF3A2A1D),
        child: ListTile(
          dense: true,
          leading: const Icon(Icons.auto_awesome_rounded, color: KzColors.claude),
          title: Text(text, style: const TextStyle(fontWeight: FontWeight.w700)),
          onTap: () {
            watch.message.value = null;
            onOpen();
          },
          trailing: TextButton(onPressed: () => watch.message.value = null, child: const Text('OK')),
        ),
      );
    });
  }
}

class KzSessionStrip extends StatelessWidget {
  const KzSessionStrip(this.watch, {super.key});
  final KzSessionWatch watch;

  static const height = 56.0;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Watch((context) {
      final list = watch.sessions.value;
      final muted = scheme.onSurface.withValues(alpha: 0.55);
      return SizedBox(
        height: height,
        child: list.isEmpty
            ? Center(child: Text('No Claude Code session is open on the PC.', style: TextStyle(color: muted)))
            : ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: list.length,
                separatorBuilder: (_, __) => const SizedBox(width: 8),
                itemBuilder: (_, i) {
                  final s = list[i];
                  final (color, label) = switch (s.status) {
                    'waiting' => (const Color(0xFFFBBF24), 'Waiting for you'),
                    'busy' => (KzColors.claude, 'Working'),
                    _ => (muted, 'Idle'),
                  };
                  return Container(
                    constraints: const BoxConstraints(minWidth: 150, maxWidth: 220),
                    padding: const EdgeInsets.symmetric(horizontal: 12),
                    decoration: BoxDecoration(
                      color: color.withValues(alpha: 0.12),
                      borderRadius: BorderRadius.circular(14),
                      border: Border.all(color: color.withValues(alpha: s.status == 'waiting' ? 0.9 : 0.35)),
                    ),
                    child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(s.project,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700)),
                          Text(label, style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: color)),
                        ]),
                  );
                },
              ),
      );
    });
  }
}
