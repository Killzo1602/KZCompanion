import 'dart:async';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:droiddeck/src/kz/kz_sessions.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

class AiUsageFeed extends StatefulWidget {
  const AiUsageFeed(this.c, {super.key, required this.builder});
  final KzController c;
  final Widget Function(List<Map<String, dynamic>>? providers, String? error) builder;

  @override
  State<AiUsageFeed> createState() => AiUsageFeedState();
}

class AiUsageFeedState extends State<AiUsageFeed> with WidgetsBindingObserver {
  static const _every = Duration(seconds: 60);

  List<Map<String, dynamic>>? _providers;
  String? _error;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _start();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _timer?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _start();
    } else if (state == AppLifecycleState.paused || state == AppLifecycleState.hidden) {
      _timer?.cancel();
    }
  }

  void _start() {
    _timer?.cancel();
    _timer = Timer.periodic(_every, (_) => load());
    load();
  }

  Future<void> load() async {
    try {
      final providers = await widget.c.client.getAiUsage();
      if (!mounted) return;
      setState(() {
        _providers = providers;
        _error = null;
      });
    } on DioException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.response?.statusCode == 404
          ? 'Update KZ Companion on your PC to see AI usage.'
          : friendlyError(e));
    } catch (e) {
      if (mounted) setState(() => _error = friendlyError(e));
    }
  }

  @override
  Widget build(BuildContext context) => widget.builder(_providers, _error);
}

class AiUsagePage extends StatefulWidget {
  const AiUsagePage({super.key, required this.c, this.sessions});
  final KzController c;

  final KzSessionWatch? sessions;

  @override
  State<AiUsagePage> createState() => _AiUsagePageState();
}

class _AiUsagePageState extends State<AiUsagePage> {
  final _feed = GlobalKey<AiUsageFeedState>();

  @override
  Widget build(BuildContext context) {
    final muted = Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6);
    return LayoutBuilder(builder: (context, page) {
      final strip = widget.sessions == null ? 0.0 : KzSessionStrip.height + 12;
      final cardHeight = (page.maxHeight - 24 - strip).clamp(240.0, 320.0);
      return RefreshIndicator(
      onRefresh: () async => _feed.currentState?.load(),
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.all(12),
        children: [
          if (widget.sessions != null)
            Padding(padding: const EdgeInsets.only(bottom: 12), child: KzSessionStrip(widget.sessions!)),
          AiUsageFeed(
            widget.c,
            key: _feed,
            builder: (providers, error) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              if (error != null)
                Padding(
                  padding: const EdgeInsets.only(bottom: 10),
                  child: Text(error, style: TextStyle(color: muted)),
                ),
              if (providers == null && error == null)
                const Padding(
                  padding: EdgeInsets.all(24),
                  child: Center(child: CircularProgressIndicator()),
                ),
              if (providers != null)
                LayoutBuilder(builder: (context, box) {
                  final cols = box.maxWidth > 520 ? 2 : 1;
                  final width = (box.maxWidth - 12 * (cols - 1)) / cols;
                  return Wrap(spacing: 12, runSpacing: 12, children: [
                    for (final p in providers)
                      SizedBox(width: width, height: cardHeight, child: AiProviderCard(p, height: cardHeight)),
                  ]);
                }),
            ]),
          ),
        ],
      ),
    );
    });
  }
}

class AiProviderCard extends StatelessWidget {
  const AiProviderCard(this.provider, {super.key, this.now, this.height});

  final double? height;
  final Map<String, dynamic> provider;
  final DateTime? now;

  @override
  Widget build(BuildContext context) {
    final muted = Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6);
    final installed = provider['installed'] == true;
    final limits = ((provider['limits'] as List?) ?? const []).cast<Map>();
    final error = provider['error'] as String?;
    final today = (provider['tokensToday'] as num?)?.toInt();
    final week = (provider['tokensWeek'] as num?)?.toInt();
    final color = provider['id'] == 'claude' ? KzColors.claude : KzColors.codex;
    final time = now ?? DateTime.now();
    final h = height ?? double.infinity;
    final roomy = h >= 230;
    final maxRing = (h - (roomy ? 170 : 110)).clamp(40.0, 112.0);
    return KzCard(
      title: '${provider['name']}',
      icon: Icons.auto_awesome_rounded,
      accent: color,
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, mainAxisSize: MainAxisSize.min, children: [
        if (!installed) Text('Not found on this PC.', style: TextStyle(color: muted)),
        if (limits.isNotEmpty)
          LayoutBuilder(builder: (context, box) {
            final cell = box.maxWidth / limits.length;
            final size = (cell - 10).clamp(40.0, maxRing);
            return Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              for (final l in limits)
                Expanded(child: _limit(l, color, size, time, muted, roomy)),
            ]);
          }),
        if (error != null)
          Padding(
            padding: const EdgeInsets.only(top: 6),
            child: Text(error, style: TextStyle(fontSize: 12, color: muted)),
          ),
        if (roomy && (today != null || week != null))
          Padding(
            padding: const EdgeInsets.only(top: 10),
            child: Text(
              [if (today != null) '${fmtTokens(today)} today', if (week != null) '${fmtTokens(week)} in 7 days']
                  .join('  ·  '),
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 13, color: muted, fontFeatures: kzDigits),
            ),
          ),
      ]),
    );
  }

  Widget _limit(Map l, Color color, double size, DateTime time, Color muted, bool roomy) {
    final percent = (l['percent'] as num).toDouble();
    final resets = DateTime.tryParse('${l['resetsAt']}');
    final window = (l['windowSeconds'] as num?)?.toInt();
    final pace = kzPace(percent, resets, window == null ? null : Duration(seconds: window), time);
    return Column(children: [
      KzRing(
        percent: percent,
        color: percent >= 90 ? KzColors.danger : color,
        value: '${percent.round()}',
        unit: '%',
        size: size,
      ),
      const SizedBox(height: 6),
      Text('${l['label']}',
          textAlign: TextAlign.center,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700)),
      Text(fmtResetIn(resets, time),
          textAlign: TextAlign.center,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(fontSize: 12.5, color: muted)),
      if (roomy && pace?.verdict != null)
        Text(pace!.verdict!,
            textAlign: TextAlign.center,
            maxLines: 2,
            style: TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.w700,
                color: switch (pace.level) { 0 => const Color(0xFF4ADE80), 2 => KzColors.danger, _ => muted })),
    ]);
  }
}

({String? verdict, int level})? kzPace(
    double percent, DateTime? resets, Duration? window, DateTime now) {
  if (resets == null || window == null || window.inSeconds <= 0) return null;
  final left = resets.difference(now);
  if (left <= Duration.zero || left > window) return null;
  final elapsed = 1 - left.inSeconds / window.inSeconds;
  if (elapsed < 0.05 || percent <= 0) return (verdict: null, level: 1);
  if (percent >= 100) return (verdict: 'Limit reached', level: 2);
  final projected = percent / elapsed;
  if (projected > 100) {
    final full = Duration(seconds: (window.inSeconds * elapsed * (100 - percent) / percent).round());
    return (verdict: 'Full in ${fmtSpan(full)}', level: 2);
  }
  return projected < 75
      ? (verdict: 'Plenty left', level: 0)
      : (verdict: 'On pace', level: 1);
}

String fmtSpan(Duration d) {
  if (d.inDays >= 1) return '${d.inDays}d ${d.inHours % 24}h';
  if (d.inHours >= 1) return '${d.inHours}h ${d.inMinutes % 60}m';
  return '${d.inMinutes}m';
}

String fmtTokens(int n) {
  if (n >= 1000000000) return '${(n / 1000000000).toStringAsFixed(1)}B';
  if (n >= 1000000) return '${(n / 1000000).toStringAsFixed(1)}M';
  if (n >= 1000) return '${(n / 1000).toStringAsFixed(1)}K';
  return '$n';
}

String fmtResetIn(DateTime? at, DateTime now) {
  if (at == null) return '';
  final left = at.difference(now);
  if (left.inMinutes < 1) return 'Resets now';
  if (left.inDays >= 1) return 'Resets in ${left.inDays}d ${left.inHours % 24}h';
  if (left.inHours >= 1) return 'Resets in ${left.inHours}h ${left.inMinutes % 60}m';
  return 'Resets in ${left.inMinutes}m';
}
