import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:droiddeck/src/kz/kz_style.dart';
import 'package:flutter/material.dart';

class KzCard extends StatelessWidget {
  const KzCard({
    super.key,
    required this.child,
    this.title,
    this.icon,
    this.trailing,
    this.padding = const EdgeInsets.all(14),
    this.expand = false,
    this.accent,
  });

  final Color? accent;

  final Widget child;
  final String? title;
  final IconData? icon;
  final Widget? trailing;
  final EdgeInsets padding;

  final bool expand;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final scale = KzPrefs.density.watch(context).padScale;
    final radius = KzPrefs.corners.watch(context).radius;
    return Container(
      padding: padding * scale,
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [
            Color.alphaBlend((accent ?? scheme.onSurface).withValues(alpha: accent == null ? 0.03 : 0.10),
                scheme.surfaceContainerLow),
            scheme.surfaceContainerLow,
          ].map((c) => c.withValues(alpha: 0.88)).toList(),
        ),
        borderRadius: BorderRadius.circular(radius),
        border: Border.all(color: scheme.onSurface.withValues(alpha: 0.10)),
      ),
      child: Material(
        type: MaterialType.transparency,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          children: [
            if (title != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 10),
                child: Row(
                  children: [
                    if (icon != null) ...[
                      Icon(icon, size: 16, color: accent ?? scheme.primary),
                      const SizedBox(width: 6),
                    ],
                    Expanded(
                      child: Text(
                        title!.toUpperCase(),
                        style: TextStyle(
                          fontSize: 11,
                          letterSpacing: 1.1,
                          fontWeight: FontWeight.w600,
                          color: scheme.onSurface.withValues(alpha: 0.6),
                        ),
                      ),
                    ),
                    if (trailing != null) trailing!,
                  ],
                ),
              ),
            if (expand) Flexible(child: child) else child,
          ],
        ),
      ),
    );
  }
}

String fmtPercent(double v) => '${v.round()}%';
String fmtTemp(double? v) => v == null ? 'Unavailable' : '${v.round()}°C';
String fmtPower(double? v) =>
    v == null ? 'Unavailable' : '${v.toStringAsFixed(v < 10 ? 1 : 0)} W';

String fmtClock(double? mhz) => mhz == null ? 'Unavailable' : '${(mhz / 1000).toStringAsFixed(2)} GHz';

String fmtVram(double? usedMb, double? totalMb) {
  if (usedMb == null) return 'Unavailable';
  final used = (usedMb / 1024).toStringAsFixed(1);
  return totalMb == null ? '$used GB' : '$used / ${(totalMb / 1024).toStringAsFixed(0)} GB';
}

class KzMetricRow extends StatelessWidget {
  const KzMetricRow(this.label, this.value, {super.key, this.available = true});
  final String label;
  final String value;
  final bool available;

  @override
  Widget build(BuildContext context) {
    final onSurface = Theme.of(context).colorScheme.onSurface;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Row(
        children: [
          Expanded(
            child: Text(
              label,
              style: TextStyle(
                fontSize: 12,
                color: onSurface.withValues(alpha: 0.65),
              ),
            ),
          ),
          Text(
            value,
            style: TextStyle(
              fontSize: 13,
              fontWeight: FontWeight.w600,
              color: available ? onSurface : onSurface.withValues(alpha: 0.4),
            ),
          ),
        ],
      ),
    );
  }
}

class KzBar extends StatelessWidget {
  const KzBar(this.percent, {super.key, this.height = 6, this.color});
  final Color? color;
  final double percent;
  final double height;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final v = (percent / 100).clamp(0.0, 1.0);
    final fill = v > 0.9 ? const Color(0xFFEF5350) : (color ?? scheme.primary);
    return ClipRRect(
      borderRadius: BorderRadius.circular(height),
      child: LinearProgressIndicator(
        value: v,
        minHeight: height,
        color: fill,
        backgroundColor: scheme.onSurface.withValues(alpha: 0.08),
      ),
    );
  }
}

class KzSparkline extends StatelessWidget {
  const KzSparkline(this.values, {super.key, this.max = 100, this.height = 44, this.color});
  final Color? color;
  final List<double> values;
  final double? max;
  final double height;

  @override
  Widget build(BuildContext context) {
    final line = color ?? Theme.of(context).colorScheme.primary;
    return SizedBox(
      height: height,
      width: double.infinity,
      child: RepaintBoundary(
        child: CustomPaint(painter: _SparkPainter(values, max, line)),
      ),
    );
  }
}

class _SparkPainter extends CustomPainter {
  _SparkPainter(this.values, this.max, this.color);
  final List<double> values;
  final double? max;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final base = Paint()
      ..color = color.withValues(alpha: 0.12)
      ..strokeWidth = 1;
    canvas.drawLine(
      Offset(0, size.height - 0.5),
      Offset(size.width, size.height - 0.5),
      base,
    );
    if (values.length < 2) return;
    var top = max ?? values.reduce((a, b) => a > b ? a : b);
    var low = max == null ? values.reduce((a, b) => a < b ? a : b) : 0.0;
    if (top - low < 1) top = low + 1;
    const slots = 60;
    final dx = size.width / (slots - 1);
    final startX = size.width - dx * (values.length - 1);
    final path = Path();
    for (var i = 0; i < values.length; i++) {
      final x = startX + dx * i;
      final y =
          size.height -
          ((values[i] - low) / (top - low)).clamp(0.0, 1.0) *
              (size.height - 3) -
          1.5;
      if (i == 0) {
        path.moveTo(x, y);
      } else {
        path.lineTo(x, y);
      }
    }
    final fill = Path.from(path)
      ..lineTo(size.width, size.height)
      ..lineTo(startX, size.height)
      ..close();
    canvas.drawPath(fill, Paint()..color = color.withValues(alpha: 0.10));
    canvas.drawPath(
      path,
      Paint()
        ..color = color
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.6
        ..strokeJoin = StrokeJoin.round,
    );
  }

  @override
  bool shouldRepaint(_SparkPainter old) =>
      !identical(old.values, values) || old.color != color;
}

class KzAppIcon extends StatelessWidget {
  const KzAppIcon(this.app, {super.key, this.size = 52});
  final KzApp app;
  final double size;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    if (app.icon != null) {
      return SizedBox(
        width: size,
        height: size,
        child: Image.memory(
          app.icon!,
          fit: BoxFit.contain,
          filterQuality: FilterQuality.medium,
          gaplessPlayback: true,
        ),
      );
    }
    return Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.14),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Text(
        app.name.isEmpty ? '?' : app.name.characters.first.toUpperCase(),
        style: TextStyle(
          fontSize: size * 0.42,
          fontWeight: FontWeight.w700,
          color: scheme.primary,
        ),
      ),
    );
  }
}

class KzAppTile extends StatelessWidget {
  const KzAppTile({
    super.key,
    required this.app,
    required this.onTap,
    this.width = 92,
  });
  final KzApp app;
  final VoidCallback onTap;
  final double width;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: width,
      child: KzTile(
        color: KzColors.apps,
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 6),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              KzAppIcon(app, size: 48),
              const SizedBox(height: 6),
              Text(
                app.name,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                textAlign: TextAlign.center,
                style: const TextStyle(fontSize: 12),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

void kzToast(BuildContext context, String message) {
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(
      SnackBar(content: Text(message), duration: const Duration(seconds: 2)),
    );
}
