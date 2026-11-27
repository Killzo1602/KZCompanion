import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_media.dart';
import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:droiddeck/src/kz/kz_style.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

class ControlsPage extends StatelessWidget {
  const ControlsPage({super.key, required this.c});
  final KzController c;

  Future<void> _power(BuildContext context, String op, {String? confirmTitle}) async {
    final needsConfirm = confirmTitle != null;
    if (needsConfirm) {
      final ok = await showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: Text(confirmTitle),
          content: const Text('Unsaved work on the PC may be lost.'),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
            FilledButton(
              style: FilledButton.styleFrom(
                  backgroundColor: const Color(0xFFC62828), foregroundColor: Colors.white),
              onPressed: () => Navigator.pop(ctx, true),
              child: Text(op == 'shutdown' ? 'Shut down' : 'Restart'),
            ),
          ],
        ),
      );
      if (ok != true) return;
    }
    try {
      await c.client.power(op, confirm: needsConfirm);
      if (!context.mounted) return;
      if (needsConfirm) {
        ScaffoldMessenger.of(context)
          ..hideCurrentSnackBar()
          ..showSnackBar(SnackBar(
            content: Text(op == 'shutdown' ? 'Shutting down in 10 s…' : 'Restarting in 10 s…'),
            duration: const Duration(seconds: 9),
            action: SnackBarAction(label: 'Cancel', onPressed: () => c.client.power('cancel')),
          ));
      }
    } catch (e) {
      if (context.mounted) kzToast(context, friendlyError(e));
    }
  }

  @override
  Widget build(BuildContext context) {
    return ListView(padding: const EdgeInsets.all(12), children: [
      SizedBox(height: 170, child: KzNowPlaying(c)),
      const SizedBox(height: 12),
      KzCard(
        title: 'Power',
        accent: KzColors.power,
        icon: Icons.power_settings_new_rounded,
        child: Row(children: [
          Expanded(child: _tile(Icons.lock_rounded, 'Lock PC', () => _power(context, 'lock'))),
          const SizedBox(width: 10),
          Expanded(child: _tile(Icons.bedtime_rounded, 'Sleep', () => _power(context, 'sleep'))),
          const SizedBox(width: 10),
          Expanded(
            child: _tile(Icons.restart_alt_rounded, 'Restart',
                () => _power(context, 'restart', confirmTitle: 'Restart the PC?'),
                danger: true),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: _tile(Icons.power_settings_new_rounded, 'Shut down',
                () => _power(context, 'shutdown', confirmTitle: 'Shut down the PC?'),
                danger: true),
          ),
        ]),
      ),
    ]);
  }

  Widget _tile(IconData icon, String label, VoidCallback onTap, {bool danger = false}) {
    final color = danger ? KzColors.danger : KzColors.power;
    return KzTile(
      color: color,
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 16),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Icon(icon, size: 28, color: color),
          const SizedBox(height: 6),
          Text(label, style: const TextStyle(fontSize: 13)),
        ]),
      ),
    );
  }
}
