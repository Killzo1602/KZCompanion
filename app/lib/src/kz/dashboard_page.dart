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
    return LayoutBuilder(builder: (context, box) {
      // Paisagem (o painel e travado assim): esquerda = CPU/GPU/RAM + favoritos; direita = audio.
      // Em tela estreita (nao deveria ocorrer) empilha, para nunca cortar nem estourar.
      if (box.maxWidth < 620) {
        return ListView(
          padding: const EdgeInsets.all(12),
          children: [
            _statsRow(),
            const SizedBox(height: 12),
            _favorites(context, expand: false),
            const SizedBox(height: 12),
            KzAudioControls(c.audio),
            const SizedBox(height: 12),
            KzMixer(c.audio),
          ],
        );
      }
      final rightWidth = (box.maxWidth * 0.42).clamp(320.0, 430.0);
      return Padding(
        padding: const EdgeInsets.all(12),
        child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              _statsRow(),
              const SizedBox(height: 12),
              Expanded(child: _favorites(context, expand: true)),
            ]),
          ),
          const SizedBox(width: 12),
          SizedBox(
            width: rightWidth,
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              KzAudioControls(c.audio),
              const SizedBox(height: 12),
              Expanded(child: KzMixer(c.audio, scroll: true)),
            ]),
          ),
        ]),
      );
    });
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
    final secondary = title == 'RAM' ? sub : fmtTemp(temp);
    return KzCard(
      title: title,
      icon: icon,
      padding: const EdgeInsets.fromLTRB(12, 12, 12, 10),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, mainAxisSize: MainAxisSize.min, children: [
        Text(fmtPercent(usage), style: const TextStyle(fontSize: 24, fontWeight: FontWeight.w700, height: 1.05)),
        const SizedBox(height: 2),
        Text(secondary ?? '',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
                fontSize: 11.5,
                fontWeight: FontWeight.w600,
                color: secondary == 'Unavailable' ? Colors.white38 : null)),
        const SizedBox(height: 6),
        KzBar(usage),
        const SizedBox(height: 6),
        KzSparkline(history, height: 26),
      ]),
    );
  }

  Widget _favorites(BuildContext context, {required bool expand}) {
    return KzCard(
      expand: expand,
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
        final wrap = Wrap(
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
        return expand ? SingleChildScrollView(child: wrap) : wrap;
      }),
    );
  }
}
