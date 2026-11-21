import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

class HardwarePage extends StatelessWidget {
  const HardwarePage({super.key, required this.c});
  final KzController c;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final s = c.signalR.systemStats.value;
      final ramOk = s.ramTotal > 0;
      final cards = <Widget>[
        KzCard(
          title: 'CPU',
          icon: Icons.memory_rounded,
          child: Column(children: [
            _big(fmtPercent(s.cpuUsage)),
            KzBar(s.cpuUsage),
            const SizedBox(height: 6),
            KzSparkline(c.cpuHistory.value),
            const SizedBox(height: 6),
            KzMetricRow('Temperature', fmtTemp(s.cpuTemp), available: s.cpuTemp != null),
            KzMetricRow('Power', fmtPower(s.cpuPower), available: s.cpuPower != null),
          ]),
        ),
        KzCard(
          title: 'GPU',
          icon: Icons.developer_board_rounded,
          child: Column(children: [
            _big(fmtPercent(s.gpuUsage)),
            KzBar(s.gpuUsage),
            const SizedBox(height: 6),
            KzSparkline(c.gpuHistory.value),
            const SizedBox(height: 6),
            KzMetricRow('Core temperature', fmtTemp(s.gpuTemp), available: s.gpuTemp != null),
            KzMetricRow('Hotspot', fmtTemp(s.gpuHotspot), available: s.gpuHotspot != null),
            KzMetricRow('Power', fmtPower(s.gpuPower), available: s.gpuPower != null),
          ]),
        ),
        KzCard(
          title: 'Memory',
          icon: Icons.storage_rounded,
          child: Column(children: [
            _big(fmtPercent(s.ramUsedPercent)),
            KzBar(s.ramUsedPercent),
            const SizedBox(height: 6),
            KzSparkline(c.ramHistory.value),
            const SizedBox(height: 6),
            KzMetricRow('Used', ramOk ? '${s.ramUsedGb.toStringAsFixed(1)} GB' : 'Unavailable', available: ramOk),
            KzMetricRow('Total', ramOk ? '${s.ramTotalGb.toStringAsFixed(0)} GB' : 'Unavailable', available: ramOk),
          ]),
        ),
        KzCard(
          title: 'Storage',
          icon: Icons.sd_storage_rounded,
          child: Column(children: [
            KzMetricRow('SSD temperature', fmtTemp(s.ssdTemp), available: s.ssdTemp != null),
            const SizedBox(height: 4),
            if (s.disks.isEmpty) const KzMetricRow('Drives', 'Unavailable', available: false),
            for (final d in s.disks) ...[
              KzMetricRow(d.name, '${d.usedPercent.round()}% used'),
              KzBar(d.usedPercent, height: 4),
              const SizedBox(height: 4),
            ],
          ]),
        ),
        KzCard(
          title: 'Fans',
          icon: Icons.air_rounded,
          child: Column(children: [
            if (s.fans.isEmpty) const KzMetricRow('Fan speed', 'Unavailable', available: false),
            for (final f in s.fans.take(8)) KzMetricRow(f.name, '${f.rpm} RPM'),
          ]),
        ),
        KzCard(
          title: 'Network',
          icon: Icons.swap_vert_rounded,
          child: Column(children: [
            KzMetricRow('Download', _rate(s.netDown)),
            KzMetricRow('Upload', _rate(s.netUp)),
          ]),
        ),
      ];
      return LayoutBuilder(builder: (context, box) {
        final cols = box.maxWidth > 700 ? 3 : 2;
        final w = (box.maxWidth - 24 - 12 * (cols - 1)) / cols;
        return ListView(padding: const EdgeInsets.all(12), children: [
          Wrap(spacing: 12, runSpacing: 12, children: [
            for (final card in cards) SizedBox(width: w, child: card),
          ]),
          if (s.cpuTemp == null)
            Padding(
              padding: const EdgeInsets.only(top: 12, left: 4),
              child: Text(
                'Some sensors (CPU temperature, fans) need KZ Companion to run as administrator on the PC.',
                style: TextStyle(
                    fontSize: 11, color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.5)),
              ),
            ),
        ]);
      });
    });
  }

  static Widget _big(String text) => Padding(
        padding: const EdgeInsets.only(bottom: 6),
        child: Align(
          alignment: Alignment.centerLeft,
          child: Text(text, style: const TextStyle(fontSize: 26, fontWeight: FontWeight.w700, height: 1)),
        ),
      );

  static String _rate(double kb) =>
      kb >= 1024 ? '${(kb / 1024).toStringAsFixed(1)} MB/s' : '${kb.round()} KB/s';
}
