import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/material.dart';

class KzTempAlert {
  KzTempAlert(SignalRService signalR, {DateTime Function()? clock}) : _clock = clock ?? DateTime.now {
    _dispose = effect(() {
      final s = signalR.systemStats.value;
      untracked(() => update(s.cpuTemp, s.gpuTemp));
    });
  }

  static const limit = 85.0;
  static const hold = Duration(seconds: 15);

  final DateTime Function() _clock;
  late final void Function() _dispose;

  final message = signal<String?>(null);

  final _since = <String, DateTime>{};
  final _dismissed = <String>{};

  void update(double? cpu, double? gpu) {
    final hot = <String>[];
    final now = _clock();
    for (final (name, temp) in [('CPU', cpu), ('GPU', gpu)]) {
      if (temp == null || temp < limit) {
        _since.remove(name);
        _dismissed.remove(name);
        continue;
      }
      final since = _since.putIfAbsent(name, () => now);
      if (now.difference(since) >= hold && !_dismissed.contains(name)) {
        hot.add('$name ${temp.round()}°C');
      }
    }
    message.value = hot.isEmpty ? null : 'High temperature: ${hot.join(', ')}';
  }

  void dismiss() {
    _dismissed.addAll(_since.keys);
    message.value = null;
  }

  void dispose() => _dispose();
}

class KzHeatBanner extends StatelessWidget {
  const KzHeatBanner(this.alert, {super.key});
  final KzTempAlert alert;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final text = alert.message.value;
      if (text == null) return const SizedBox.shrink();
      return Material(
        color: const Color(0xFF3A1D1D),
        child: ListTile(
          dense: true,
          leading: const Icon(Icons.local_fire_department_rounded, color: Color(0xFFEF5350)),
          title: Text(text),
          trailing: TextButton(onPressed: alert.dismiss, child: const Text('Dismiss')),
        ),
      );
    });
  }
}
