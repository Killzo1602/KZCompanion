import 'dart:async';

import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

const _days = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const _months = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

String fmtLongDate(DateTime d) => '${_days[d.weekday - 1]} ${d.day} ${_months[d.month - 1]}';

class KzClock extends StatefulWidget {
  const KzClock({super.key});

  @override
  State<KzClock> createState() => _KzClockState();
}

class _KzClockState extends State<KzClock> {
  late final Timer _timer;

  @override
  void initState() {
    super.initState();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) => setState(() {}));
  }

  @override
  void dispose() {
    _timer.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final now = DateTime.now();
    final time = MaterialLocalizations.of(context)
        .formatTimeOfDay(TimeOfDay.fromDateTime(now), alwaysUse24HourFormat: MediaQuery.alwaysUse24HourFormatOf(context));
    return KzCard(
      expand: true,
      child: Center(
        child: FittedBox(
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Text(time,
                style: const TextStyle(
                    fontSize: 72, fontWeight: FontWeight.w200, height: 1, fontFeatures: kzDigits)),
            const SizedBox(height: 6),
            Text(fmtLongDate(now),
                style: TextStyle(fontSize: 16, color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.65))),
          ]),
        ),
      ),
    );
  }
}
