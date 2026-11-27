import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

class HardwarePage extends StatelessWidget {
  const HardwarePage({super.key, required this.c});
  final KzController c;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final gap = KzPrefs.density.value.gap;
      final s = c.signalR.systemStats.value;
      final ramOk = s.ramTotal > 0;

      Widget gauge(String title, IconData icon, Color color, double usage, List<double> history, List<Widget> rows) =>
          _FitCard(
            title: title,
            icon: icon,
            color: color,
            child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              Row(children: [
                KzRing(percent: usage, color: color, value: '${usage.round()}', unit: '%', size: 74),
                const SizedBox(width: 12),
                Expanded(child: KzSparkline(history, height: 48, color: color)),
              ]),
              const SizedBox(height: 6),
              ...rows,
            ]),
          );

      final top = [
        gauge('CPU', Icons.memory_rounded, KzColors.cpu, s.cpuUsage, c.cpuHistory.value, [
          KzMetricRow('Temperature', fmtTemp(s.cpuTemp), available: s.cpuTemp != null),
          KzMetricRow('Power', fmtPower(s.cpuPower), available: s.cpuPower != null),
          KzMetricRow('Clock', fmtClock(s.cpuClock), available: s.cpuClock != null),
          if (s.cpuTemp == null && s.cpuPower == null)
            Text(
                s.cpuDriverMissing
                    ? 'PC tray: Install CPU sensor driver, then restart as administrator.'
                    : (s.sensorsElevated
                        ? 'The CPU does not report this sensor.'
                        : 'PC tray: Restart as administrator.'),
                style: TextStyle(fontSize: 10.5, color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.5))),
        ]),
        gauge('GPU', Icons.developer_board_rounded, KzColors.gpu, s.gpuUsage, c.gpuHistory.value, [
          KzMetricRow('Core / hotspot',
              s.gpuHotspot == null ? fmtTemp(s.gpuTemp) : '${fmtTemp(s.gpuTemp)} / ${fmtTemp(s.gpuHotspot)}',
              available: s.gpuTemp != null),
          KzMetricRow('Power', fmtPower(s.gpuPower), available: s.gpuPower != null),
          KzMetricRow('Clock', fmtClock(s.gpuClock), available: s.gpuClock != null),
          KzMetricRow('Video memory', fmtVram(s.gpuMemUsed, s.gpuMemTotal), available: s.gpuMemUsed != null),
        ]),
        gauge('Memory', Icons.storage_rounded, KzColors.ram, s.ramUsedPercent, c.ramHistory.value, [
          KzMetricRow('Used', ramOk ? '${s.ramUsedGb.toStringAsFixed(1)} GB' : 'Unavailable', available: ramOk),
          KzMetricRow('Total', ramOk ? '${s.ramTotalGb.toStringAsFixed(0)} GB' : 'Unavailable', available: ramOk),
        ]),
      ];

      Widget list(String title, IconData icon, Color color, List<Widget> rows) => _FitCard(
            title: title,
            icon: icon,
            color: color,
            child: Column(
                mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: rows),
          );

      final bottom = [
        list('Storage', Icons.sd_storage_rounded, KzColors.storage, [
          if (s.drives.isEmpty)
            const KzMetricRow('Drive temperature', 'Unavailable', available: false)
          else
            for (final d in s.drives) KzMetricRow(d.name, fmtTemp(d.temp)),
          if (s.disks.isEmpty) const KzMetricRow('Drives', 'Unavailable', available: false),
          for (final d in s.disks) ...[
            KzMetricRow(d.name, '${d.usedPercent.round()}% used'),
            KzBar(d.usedPercent, height: 4, color: KzColors.storage),
          ],
        ]),
        list('Fans', Icons.air_rounded, KzColors.fans, [
          if (s.fans.isEmpty) const KzMetricRow('Fan speed', 'Unavailable', available: false),
          for (final f in s.fans.take(6)) KzMetricRow(f.name, '${f.rpm} RPM'),
        ]),
        list('Network', Icons.swap_vert_rounded, KzColors.network, [
          KzMetricRow('Download', _rate(s.netDown)),
          KzMetricRow('Upload', _rate(s.netUp)),
          if (s.netAdapter.isNotEmpty) KzMetricRow('Adapter', s.netAdapter),
        ]),
      ];

      Widget row(List<Widget> cards) => Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            for (var i = 0; i < cards.length; i++) ...[
              if (i > 0) SizedBox(width: gap),
              Expanded(child: cards[i]),
            ],
          ]);

      return Padding(
        padding: EdgeInsets.all(gap),
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Expanded(flex: 8, child: row(top)),
          SizedBox(height: gap),
          Expanded(flex: 5, child: row(bottom)),
        ]),
      );
    });
  }

  static String _rate(double kb) =>
      kb >= 1024 ? '${(kb / 1024).toStringAsFixed(1)} MB/s' : '${kb.round()} KB/s';
}

class _FitCard extends StatelessWidget {
  const _FitCard({required this.title, required this.icon, required this.child, this.color});
  final String title;
  final IconData icon;
  final Color? color;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return KzCard(
      expand: true,
      title: title,
      icon: icon,
      accent: color,
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 8),
      child: SizedBox.expand(
        child: LayoutBuilder(
          builder: (_, box) => FittedBox(
            fit: BoxFit.scaleDown,
            alignment: Alignment.topCenter,
            child: SizedBox(width: box.maxWidth, child: child),
          ),
        ),
      ),
    );
  }
}
