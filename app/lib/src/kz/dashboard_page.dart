import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_audio_widgets.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

class DashboardPage extends StatelessWidget {
  const DashboardPage({super.key, required this.c, required this.onOpenApps});
  final KzController c;
  final VoidCallback onOpenApps;

  @override
  Widget build(BuildContext context) {
    // Ordem: CPU/GPU/RAM, favoritos, controles de audio, mixer. Rola na vertical; cada bloco
    // tem o seu proprio Watch, entao arrastar um slider nao reconstrói o resto do painel.
    return ListView(
      padding: const EdgeInsets.all(12),
      children: [
        _statsRow(),
        const SizedBox(height: 12),
        _favorites(context),
        const SizedBox(height: 12),
        KzAudioControls(c.audio),
        const SizedBox(height: 12),
        KzMixer(c.audio),
        const SizedBox(height: 4),
      ],
    );
  }

  Widget _statsRow() {
    return Watch((context) {
      final s = c.signalR.systemStats.value;
      return IntrinsicHeight(
        child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Expanded(child: _stat('CPU', Icons.memory_rounded, s.cpuUsage, s.cpuTemp, c.cpuHistory.value)),
          const SizedBox(width: 12),
          Expanded(child: _stat('GPU', Icons.developer_board_rounded, s.gpuUsage, s.gpuTemp, c.gpuHistory.value)),
          const SizedBox(width: 12),
          Expanded(
            child: _stat('RAM', Icons.storage_rounded, s.ramUsedPercent, null, c.ramHistory.value,
                sub: s.ramTotal > 0
                    ? '${s.ramUsedGb.toStringAsFixed(1)} / ${s.ramTotalGb.toStringAsFixed(0)} GB'
                    : null),
          ),
        ]),
      );
    });
  }

  Widget _stat(String title, IconData icon, double usage, double? temp, List<double> history,
      {String? sub}) {
    return KzCard(
      title: title,
      icon: icon,
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
        Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
          Text(fmtPercent(usage), style: const TextStyle(fontSize: 26, fontWeight: FontWeight.w700, height: 1)),
          const Spacer(),
          if (title != 'RAM')
            Text(fmtTemp(temp),
                style: TextStyle(
                    fontSize: 12,
                    color: temp == null ? Colors.white38 : null,
                    fontWeight: FontWeight.w600))
          else if (sub != null)
            Text(sub, style: const TextStyle(fontSize: 12)),
        ]),
        const SizedBox(height: 8),
        KzBar(usage),
        const SizedBox(height: 8),
        KzSparkline(history, height: 30),
      ]),
    );
  }

  Widget _favorites(BuildContext context) {
    return KzCard(
      title: 'Favorites',
      icon: Icons.star_rounded,
      trailing: TextButton(onPressed: onOpenApps, child: const Text('All apps')),
      child: Watch((context) {
        final favs = c.apps.value.where((a) => a.favorite).toList();
        if (favs.isEmpty) {
          return Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: Center(
              child: Text(
                c.apps.value.isEmpty
                    ? 'No applications yet. Add them from the Windows configurator.'
                    : 'Star applications in the Windows configurator to pin them here.',
                textAlign: TextAlign.center,
                style: TextStyle(color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.55)),
              ),
            ),
          );
        }
        return Wrap(
          spacing: 10,
          runSpacing: 10,
          children: [
            for (final a in favs)
              KzAppTile(
                app: a,
                onTap: () async {
                  final err = await c.launch(a);
                  if (context.mounted) kzToast(context, err ?? 'Opening ${a.name}…');
                },
              ),
          ],
        );
      }),
    );
  }
}
