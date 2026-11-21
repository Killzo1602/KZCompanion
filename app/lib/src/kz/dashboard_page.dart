import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_media.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

class DashboardPage extends StatelessWidget {
  const DashboardPage({super.key, required this.c, required this.onOpenApps});
  final KzController c;
  final VoidCallback onOpenApps;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            _statsRow(),
            const SizedBox(height: 12),
            Expanded(child: _favorites(context)),
          ]),
        ),
        const SizedBox(width: 12),
        SizedBox(
          width: 250,
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            _connection(context),
            const SizedBox(height: 12),
            Expanded(
              child: KzCard(
                title: 'Media',
                icon: Icons.music_note_rounded,
                child: Column(mainAxisSize: MainAxisSize.min, children: [
                  KzMediaControls(c, compact: true),
                  const SizedBox(height: 4),
                  KzVolume(c),
                ]),
              ),
            ),
          ]),
        ),
      ]),
    );
  }

  Widget _connection(BuildContext context) {
    return Watch((context) {
      final status = c.signalR.connectionStatus.value;
      final ok = status == 'Connected';
      final connecting = status.startsWith('Connecting') || status.startsWith('Reconnecting');
      final color = ok
          ? const Color(0xFF4ADE80)
          : connecting
              ? const Color(0xFFFBBF24)
              : const Color(0xFFEF5350);
      final label = ok ? 'PC connected' : connecting ? 'Connecting…' : 'PC offline';
      final url = c.signalR.currentUrl.value;
      return KzCard(
        child: Row(children: [
          Container(
            width: 10,
            height: 10,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
              Text(
                ok ? _host(url) : 'Retrying automatically',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                    fontSize: 12,
                    color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6)),
              ),
            ]),
          ),
        ]),
      );
    });
  }

  String _host(String url) {
    final u = Uri.tryParse(url);
    return u == null || u.host.isEmpty ? url : '${u.host}:${u.port}';
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
      child: Flexible(
        child: Watch((context) {
          final favs = c.apps.value.where((a) => a.favorite).toList();
          if (favs.isEmpty) {
            return Center(
              child: Text(
                c.apps.value.isEmpty
                    ? 'No applications yet.\nAdd them from the Windows configurator.'
                    : 'Mark applications as favorites in the Windows configurator.',
                textAlign: TextAlign.center,
                style: TextStyle(color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.55)),
              ),
            );
          }
          return SingleChildScrollView(
            child: Wrap(
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
            ),
          );
        }),
      ),
    );
  }
}
