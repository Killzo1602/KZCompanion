import 'dart:math' as math;

import 'package:flutter/material.dart';

class KzColors {
  static const cpu = Color(0xFF2DD4BF);
  static const gpu = Color(0xFF84CC16);
  static const ram = Color(0xFFA78BFA);
  static const hot = Color(0xFFF97316);
  static const media = Color(0xFFD946EF);
  static const claude = Color(0xFFD97757);
  static const codex = Color(0xFFE2E8F0);
  static const danger = Color(0xFFEF5350);
  static const audio = Color(0xFF38BDF8);
  static const apps = Color(0xFFFBBF24);
  static const power = Color(0xFF818CF8);
  static const storage = Color(0xFFF59E0B);
  static const fans = Color(0xFF22D3EE);
  static const network = Color(0xFF60A5FA);
}

const kzDigits = [FontFeature.tabularFigures()];

class KzRing extends StatelessWidget {
  const KzRing({
    super.key,
    required this.percent,
    required this.color,
    required this.value,
    this.unit = '',
    this.size = 96,
  });

  final double percent;
  final Color color;
  final String value;
  final String unit;
  final double size;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return SizedBox(
      width: size,
      height: size,
      child: TweenAnimationBuilder<double>(
        tween: Tween(end: (percent / 100).clamp(0.0, 1.0)),
        duration: const Duration(milliseconds: 500),
        curve: Curves.easeOutCubic,
        builder: (context, sweep, child) => CustomPaint(
          painter: _RingPainter(sweep, color, scheme.onSurface),
          child: child,
        ),
        child: Center(
          child: Padding(
            padding: EdgeInsets.all(size * 0.18),
            child: FittedBox(
              child: Text.rich(
                TextSpan(children: [
                  TextSpan(
                    text: value,
                    style: TextStyle(fontSize: 28, fontWeight: FontWeight.w700, color: scheme.onSurface),
                  ),
                  TextSpan(
                    text: unit,
                    style: TextStyle(fontSize: 14, color: scheme.onSurface.withValues(alpha: 0.55)),
                  ),
                ]),
                style: const TextStyle(fontFeatures: kzDigits),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _RingPainter extends CustomPainter {
  _RingPainter(this.sweep, this.color, this.ink);
  final double sweep;
  final Color color;
  final Color ink;

  @override
  void paint(Canvas canvas, Size size) {
    final stroke = size.shortestSide * 0.09;
    final center = size.center(Offset.zero);
    final radius = (size.shortestSide - stroke) / 2 - stroke * 0.35;
    final rect = Rect.fromCircle(center: center, radius: radius);
    Paint line(Color c) => Paint()
      ..color = c
      ..style = PaintingStyle.stroke
      ..strokeWidth = stroke
      ..strokeCap = StrokeCap.round;

    canvas.drawCircle(center, radius, line(ink.withValues(alpha: 0.09)));
    if (sweep > 0.001) {
      final angle = sweep * 2 * math.pi;
      canvas.drawArc(rect, -math.pi / 2, angle, false,
          line(color.withValues(alpha: 0.55))..maskFilter = MaskFilter.blur(BlurStyle.normal, stroke * 0.7));
      canvas.drawArc(rect, -math.pi / 2, angle, false, line(color));
    }
  }

  @override
  bool shouldRepaint(_RingPainter old) => old.sweep != sweep || old.color != color || old.ink != ink;
}
