import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:droiddeck/src/kz/running_apps_section.dart';
import 'package:flutter/material.dart';

/// Lançador: "Running now" (apps abertos no PC) e os aplicativos salvos, agrupados, com o ícone original.
class AppsPage extends StatefulWidget {
  const AppsPage({super.key, required this.c});
  final KzController c;

  @override
  State<AppsPage> createState() => _AppsPageState();
}

class _AppsPageState extends State<AppsPage> with WidgetsBindingObserver {
  KzController get c => widget.c;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (KzPrefs.showRunningApps.value) c.runningApps.start();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    c.runningApps.stop();
    super.dispose();
  }

  /// App em segundo plano: o PC para de observar janelas; ao voltar, retoma e atualiza.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      if (KzPrefs.showRunningApps.value) c.runningApps.start();
    } else if (state == AppLifecycleState.paused || state == AppLifecycleState.hidden) {
      c.runningApps.stop();
    }
  }

  Future<void> _refresh() async {
    await Future.wait([c.refreshApps(), if (KzPrefs.showRunningApps.value) c.runningApps.refresh()]);
  }

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final showRunning = KzPrefs.showRunningApps.value;
      // O ajuste em Appearance liga e desliga a observacao na hora.
      if (showRunning && !c.runningApps.active) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted && KzPrefs.showRunningApps.value) c.runningApps.start();
        });
      } else if (!showRunning && c.runningApps.active) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted && !KzPrefs.showRunningApps.value) c.runningApps.stop();
        });
      }

      final apps = c.apps.value;
      final muted = Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6);
      const defaultGroup = 'Applications';
      const favGroup = 'Favorites';
      final groups = <String, List<KzApp>>{};
      for (final a in apps.where((a) => a.favorite)) {
        groups.putIfAbsent(favGroup, () => []).add(a);
      }
      for (final a in apps.where((a) => !a.favorite)) {
        groups.putIfAbsent(a.group.isEmpty ? defaultGroup : a.group, () => []).add(a);
      }
      final names = groups.keys.toList()
        ..sort((a, b) {
          if (a == favGroup) return -1;
          if (b == favGroup) return 1;
          if (a == defaultGroup) return -1;
          if (b == defaultGroup) return 1;
          return a.toLowerCase().compareTo(b.toLowerCase());
        });

      return RefreshIndicator(
        onRefresh: _refresh,
        child: ListView(
          padding: const EdgeInsets.all(12),
          children: [
            if (showRunning) RunningAppsSection(store: c.runningApps, audio: c.audio),
            if (apps.isEmpty) _emptyLibrary(context, muted),
            for (final g in names) ...[
              Padding(
                padding: const EdgeInsets.fromLTRB(4, 6, 0, 8),
                child: Text(g.toUpperCase(),
                    style: TextStyle(fontSize: 11, letterSpacing: 1.1, fontWeight: FontWeight.w600, color: muted)),
              ),
              Wrap(spacing: 10, runSpacing: 10, children: [
                for (final a in groups[g]!)
                  KzAppTile(
                    app: a,
                    width: 104,
                    onTap: () async {
                      final err = await c.launch(a);
                      if (context.mounted) kzToast(context, err ?? 'Opening ${a.name}…');
                    },
                  ),
              ]),
              const SizedBox(height: 10),
            ],
          ],
        ),
      );
    });
  }

  Widget _emptyLibrary(BuildContext context, Color muted) {
    final err = c.appsError.value;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 24),
      child: Center(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Icon(Icons.apps_rounded, size: 40, color: Theme.of(context).colorScheme.primary),
          const SizedBox(height: 10),
          Text(err != null ? 'Could not load applications' : 'No applications yet',
              style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600)),
          const SizedBox(height: 4),
          Text(
            err != null
                ? 'Check the connection to your PC.'
                : 'On your PC open KZ Companion, then Applications, then Add Application.',
            style: TextStyle(color: muted),
          ),
          const SizedBox(height: 12),
          OutlinedButton.icon(
              onPressed: c.refreshApps, icon: const Icon(Icons.refresh), label: const Text('Refresh')),
        ]),
      ),
    );
  }
}
