import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:flutter/material.dart';

SliderThemeData kzSliderTheme(BuildContext context, {Color color = KzColors.audio, double track = 10}) {
  final ink = Theme.of(context).colorScheme.onSurface;
  return SliderTheme.of(context).copyWith(
    trackHeight: track,
    activeTrackColor: color,
    inactiveTrackColor: ink.withValues(alpha: 0.10),
    thumbColor: Color.alphaBlend(Colors.white.withValues(alpha: 0.85), color),
    overlayColor: color.withValues(alpha: 0.18),
    thumbShape: RoundSliderThumbShape(enabledThumbRadius: track * 0.9, elevation: 3),
    overlayShape: RoundSliderOverlayShape(overlayRadius: track * 1.8),
  );
}

class KzTile extends StatelessWidget {
  const KzTile({
    super.key,
    required this.child,
    required this.onTap,
    this.color = KzColors.audio,
    this.active = false,
    this.radius = 14,
  });

  final Widget child;
  final VoidCallback? onTap;
  final Color color;

  final bool active;
  final double radius;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      color: Color.alphaBlend(color.withValues(alpha: active ? 0.22 : 0.07), scheme.surfaceContainer),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(radius),
        side: BorderSide(color: color.withValues(alpha: active ? 0.85 : 0.22), width: active ? 1.4 : 1),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(onTap: onTap, splashColor: color.withValues(alpha: 0.2), child: child),
    );
  }
}
