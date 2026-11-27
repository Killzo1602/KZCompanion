import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/grid/kz_grid_layout.dart';
import 'package:droiddeck/src/kz/grid/kz_grid_view.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:droiddeck/src/kz/kz_audio_widgets.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

class DashboardPage extends StatelessWidget {
  const DashboardPage({super.key, required this.c, required this.onOpenApps});
  final KzController c;
  final VoidCallback onOpenApps;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      if (KzGrid.editing.value || KzGrid.layout.value != null) {
        return KzGridView(c: c, onOpenApps: onOpenApps);
      }
      final gap = KzPrefs.density.value.gap;
      final stats = KzPrefs.showStats.value;
      final favs = KzPrefs.showFavorites.value;
      final audio = KzPrefs.showAudio.value;
      if (!stats && !favs && !audio) {
        return Center(
          child: Text(
            'All dashboard sections are hidden.\nTurn them on in Settings → Appearance.',
            textAlign: TextAlign.center,
            style: TextStyle(color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.55)),
          ),
        );
      }
      return LayoutBuilder(builder: (context, box) {
        if (box.maxWidth < 620) {
          return ListView(
            padding: EdgeInsets.all(gap),
            children: [
              if (stats) ...[_statsRow(gap), SizedBox(height: gap)],
              if (favs) ...[KzFavoritesCard(c, onOpenApps: onOpenApps), SizedBox(height: gap)],
              if (audio) ...[
                KzAudioControls(c.audio),
                SizedBox(height: gap),
                KzMixer(c.audio),
              ],
            ],
          );
        }
        final rightWidth = (box.maxWidth * 0.42).clamp(320.0, 430.0);
        final left = stats || favs;
        final leftColumn = Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          if (stats) _statsRow(gap),
          if (stats && favs) SizedBox(height: gap),
          if (favs) Expanded(child: KzFavoritesCard(c, onOpenApps: onOpenApps, expand: true)),
          if (!favs) const Spacer(),
        ]);
        final rightColumn = Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          if (stats) SizedBox(height: _statsHeight, child: KzAudioControls(c.audio)) else KzAudioControls(c.audio),
          SizedBox(height: gap),
          Expanded(child: KzMixer(c.audio, scroll: true)),
        ]);
        return Padding(
          padding: EdgeInsets.all(gap),
          child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            if (left) Expanded(child: leftColumn),
            if (left && audio) SizedBox(width: gap),
            if (audio) left ? SizedBox(width: rightWidth, child: rightColumn) : Expanded(child: rightColumn),
          ]),
        );
      });
    });
  }

  static const _statsHeight = 168.0;

  Widget _statsRow(double gap) {
    return SizedBox(
      height: _statsHeight,
      child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Expanded(child: KzStatCard(c, KzStat.cpu)),
        SizedBox(width: gap),
        Expanded(child: KzStatCard(c, KzStat.gpu)),
        SizedBox(width: gap),
        Expanded(child: KzStatCard(c, KzStat.ram)),
      ]),
    );
  }
}

enum KzStat { cpu, gpu, ram }

class KzStatCard extends StatelessWidget {
  const KzStatCard(this.c, this.stat, {super.key});
  final KzController c;
  final KzStat stat;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final s = c.signalR.systemStats.value;
      final (title, color, usage, temp, detail) = switch (stat) {
        KzStat.cpu => ('CPU', KzColors.cpu, s.cpuUsage, s.cpuTemp, fmtTemp(s.cpuTemp)),
        KzStat.gpu => ('GPU', KzColors.gpu, s.gpuUsage, s.gpuTemp, fmtTemp(s.gpuTemp)),
        KzStat.ram => (
            'RAM',
            KzColors.ram,
            s.ramUsedPercent,
            null,
            s.ramTotal > 0 ? '${s.ramUsedGb.toStringAsFixed(1)} / ${s.ramTotalGb.toStringAsFixed(0)} GB' : ''
          ),
      };
      final muted = Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.55);
      return KzCard(
        expand: true,
        accent: color,
        padding: const EdgeInsets.fromLTRB(8, 8, 8, 8),
        child: SizedBox.expand(
          child: Column(children: [
            Expanded(
              child: LayoutBuilder(
                builder: (_, box) => Center(
                  child: KzRing(
                    percent: usage,
                    color: temp != null && temp >= 85 ? KzColors.danger : color,
                    value: '${usage.round()}',
                    unit: '%',
                    size: box.biggest.shortestSide,
                  ),
                ),
              ),
            ),
            const SizedBox(height: 4),
            Text(title,
                style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, letterSpacing: 1.4, color: color)),
            Text(detail == 'Unavailable' ? 'No temperature' : detail,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 11.5, color: muted, fontFeatures: kzDigits)),
          ]),
        ),
      );
    });
  }
}

class KzFavoritesCard extends StatelessWidget {
  const KzFavoritesCard(this.c, {super.key, required this.onOpenApps, this.expand = false});
  final KzController c;
  final VoidCallback onOpenApps;
  final bool expand;

  @override
  Widget build(BuildContext context) {
    return KzCard(
      expand: expand,
      title: 'Favorites',
      accent: KzColors.apps,
      icon: Icons.star_rounded,
      trailing: InkWell(
        onTap: onOpenApps,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
          child: Text('All apps',
              style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: KzColors.apps)),
        ),
      ),
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
