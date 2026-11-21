import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

/// Lançador: aplicativos salvos no PC, agrupados, com o ícone original.
class AppsPage extends StatelessWidget {
  const AppsPage({super.key, required this.c});
  final KzController c;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final apps = c.apps.value;
      final muted = Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6);
      if (apps.isEmpty) {
        final err = c.appsError.value;
        return Center(
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
        );
      }
      const defaultGroup = 'Applications';
      final groups = <String, List<KzApp>>{};
      for (final a in apps) {
        groups.putIfAbsent(a.group.isEmpty ? defaultGroup : a.group, () => []).add(a);
      }
      final names = groups.keys.toList()
        ..sort((a, b) {
          if (a == defaultGroup) return -1;
          if (b == defaultGroup) return 1;
          return a.toLowerCase().compareTo(b.toLowerCase());
        });
      return RefreshIndicator(
        onRefresh: c.refreshApps,
        child: ListView(
          padding: const EdgeInsets.all(12),
          children: [
            for (final g in names) ...[
              Padding(
                padding: const EdgeInsets.fromLTRB(4, 6, 0, 8),
                child: Text(g.toUpperCase(),
                    style: TextStyle(
                        fontSize: 11, letterSpacing: 1.1, fontWeight: FontWeight.w600, color: muted)),
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
}
