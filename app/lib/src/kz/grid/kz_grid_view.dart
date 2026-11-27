import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/ai_usage_page.dart';
import 'package:droiddeck/src/kz/dashboard_page.dart';
import 'package:droiddeck/src/kz/grid/kz_grid_layout.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_audio_widgets.dart';
import 'package:droiddeck/src/kz/kz_clock.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_media.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

Size _designMin(GridType type) => switch (type) {
      GridType.cpu || GridType.gpu || GridType.ram => const Size(120, 100),
      GridType.favorites => const Size(210, 170),
      GridType.audio => const Size(320, 140),
      GridType.mixer => const Size(300, 150),
      GridType.media => const Size(260, 150),
      GridType.claude || GridType.codex => const Size(190, 100),
      GridType.clock => const Size(80, 40),
    };

class _Fit extends StatelessWidget {
  const _Fit(this.min, this.child);
  final Size min;
  final Widget child;

  @override
  Widget build(BuildContext context) => LayoutBuilder(builder: (_, box) {
        final scale = [1.0, box.maxWidth / min.width, box.maxHeight / min.height]
            .reduce((a, b) => a < b ? a : b);
        if (scale >= 1) return child;
        return FittedBox(
          fit: BoxFit.fill,
          child: SizedBox(width: box.maxWidth / scale, height: box.maxHeight / scale, child: child),
        );
      });
}

Widget gridWidget(GridType type, KzController c, VoidCallback onOpenApps) {
  Widget scroll(Widget child) => LayoutBuilder(
        builder: (_, box) => SingleChildScrollView(
          child: ConstrainedBox(constraints: BoxConstraints(minHeight: box.maxHeight), child: child),
        ),
      );
  return switch (type) {
    GridType.cpu => KzStatCard(c, KzStat.cpu),
    GridType.gpu => KzStatCard(c, KzStat.gpu),
    GridType.ram => KzStatCard(c, KzStat.ram),
    GridType.favorites => KzFavoritesCard(c, onOpenApps: onOpenApps, expand: true),
    GridType.audio => scroll(KzAudioControls(c.audio)),
    GridType.mixer => KzMixer(c.audio, scroll: true),
    GridType.media => KzNowPlaying(c),
    GridType.claude => LayoutBuilder(
        builder: (_, box) => SingleChildScrollView(
          child: ConstrainedBox(
            constraints: BoxConstraints(minHeight: box.maxHeight),
            child: AiUsageFeed(c, builder: (p, e) => _ai('claude', 'Claude Code', p, e, box.maxHeight)),
          ),
        ),
      ),
    GridType.codex => LayoutBuilder(
        builder: (_, box) => SingleChildScrollView(
          child: ConstrainedBox(
            constraints: BoxConstraints(minHeight: box.maxHeight),
            child: AiUsageFeed(c, builder: (p, e) => _ai('codex', 'Codex', p, e, box.maxHeight)),
          ),
        ),
      ),
    GridType.clock => const KzClock(),
  };
}

Widget _ai(String id, String name, List<Map<String, dynamic>>? providers, String? error, double height) {
  final found = providers?.where((p) => p['id'] == id).firstOrNull;
  if (found != null) return AiProviderCard(found, height: height);
  return KzCard(
    title: name,
    icon: Icons.auto_awesome_rounded,
    child: Text(error ?? 'Loading…', style: const TextStyle(fontSize: 12)),
  );
}

class KzGridView extends StatefulWidget {
  const KzGridView({super.key, required this.c, required this.onOpenApps});
  final KzController c;
  final VoidCallback onOpenApps;

  @override
  State<KzGridView> createState() => _KzGridViewState();
}

class _KzGridViewState extends State<KzGridView> {
  List<GridItem>? _draft;
  bool _changed = false;

  GridItem? _grabbed;
  Offset _delta = Offset.zero;

  void _replace(GridItem item) {
    final list = _draft!;
    final i = list.indexWhere((e) => e.type == item.type);
    if (list[i] == item || !gridFits(list, item)) return;
    setState(() {
      list[i] = item;
      _changed = true;
    });
  }

  void _drag(Offset delta, Size cell, {required bool resize}) {
    final from = _grabbed;
    if (from == null) return;
    _delta += delta;
    final dx = (_delta.dx / cell.width).round();
    final dy = (_delta.dy / cell.height).round();
    _replace(resize
        ? from.copyWith(
            w: (from.w + dx).clamp(from.type.minW, gridCols - from.x),
            h: (from.h + dy).clamp(from.type.minH, gridRows - from.y))
        : from.copyWith(
            x: (from.x + dx).clamp(0, gridCols - from.w), y: (from.y + dy).clamp(0, gridRows - from.h)));
  }

  Future<void> _add() async {
    final used = _draft!.map((e) => e.type).toSet();
    final type = await showModalBottomSheet<GridType>(
      context: context,
      builder: (ctx) => ListView(shrinkWrap: true, children: [
        for (final t in GridType.values.where((t) => !used.contains(t)))
          ListTile(
            dense: true,
            title: Text(t.label),
            enabled: gridFreeSpot(_draft!, t) != null,
            subtitle: gridFreeSpot(_draft!, t) == null ? const Text('No room: shrink or remove something') : null,
            onTap: () => Navigator.pop(ctx, t),
          ),
      ]),
    );
    if (type == null || !mounted || _draft == null) return;
    final spot = gridFreeSpot(_draft!, type);
    if (spot == null) return;
    setState(() {
      _draft!.add(spot);
      _changed = true;
    });
  }

  Future<void> _done() async {
    if (_changed) await KzGrid.save(_draft!);
    KzGrid.editing.value = false;
  }

  Future<void> _reset() async {
    await KzGrid.reset();
    KzGrid.editing.value = false;
  }

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final editing = KzGrid.editing.value;
      final saved = KzGrid.layout.value;
      final gap = KzPrefs.density.value.gap;
      if (editing) {
        _draft ??= [...saved ?? gridStart];
      } else {
        _draft = null;
        _changed = false;
        _grabbed = null;
      }
      final items = _draft ?? saved ?? gridStart;
      return Column(children: [
        if (editing)
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 4, 12, 0),
            child: Row(children: [
              const Expanded(
                child: Text('Drag to move, drag the corner to resize',
                    maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 12)),
              ),
              TextButton(onPressed: _add, child: const Text('Add')),
              TextButton(onPressed: _reset, child: const Text('Default')),
              FilledButton(onPressed: _done, child: const Text('Done')),
            ]),
          ),
        Expanded(
          child: Padding(
            padding: EdgeInsets.all(gap / 2),
            child: LayoutBuilder(builder: (context, box) {
              final cell = Size(box.maxWidth / gridCols, box.maxHeight / gridRows);
              return Stack(children: [
                for (final item in items)
                  Positioned(
                    key: ValueKey(item.type),
                    left: item.x * cell.width,
                    top: item.y * cell.height,
                    width: item.w * cell.width,
                    height: item.h * cell.height,
                    child: Padding(
                      padding: EdgeInsets.all(gap / 2),
                      child: _tile(item, cell, editing),
                    ),
                  ),
              ]);
            }),
          ),
        ),
      ]);
    });
  }

  Widget _tile(GridItem item, Size cell, bool editing) {
    final content = ClipRect(
      child: _Fit(_designMin(item.type), gridWidget(item.type, widget.c, widget.onOpenApps)),
    );
    final scheme = Theme.of(context).colorScheme;
    return Stack(fit: StackFit.expand, children: [
      IgnorePointer(ignoring: editing, child: content),
      if (editing) ...[
        GestureDetector(
          key: ValueKey('move-${item.type.name}'),
          behavior: HitTestBehavior.opaque,
          onPanStart: (_) {
            _grabbed = item;
            _delta = Offset.zero;
          },
          onPanUpdate: (d) => _drag(d.delta, cell, resize: false),
          child: DecoratedBox(
            decoration: BoxDecoration(
              color: scheme.primary.withValues(alpha: 0.10),
              border: Border.all(color: scheme.primary, width: 1.5),
              borderRadius: BorderRadius.circular(KzPrefs.corners.value.radius),
            ),
          ),
        ),
        Positioned(
          top: 0,
          right: 0,
          child: IconButton.filledTonal(
            key: ValueKey('remove-${item.type.name}'),
            visualDensity: VisualDensity.compact,
            iconSize: 16,
            icon: const Icon(Icons.close_rounded),
            onPressed: () => setState(() {
              _draft!.removeWhere((e) => e.type == item.type);
              _changed = true;
            }),
          ),
        ),
        Positioned(
          right: 0,
          bottom: 0,
          child: GestureDetector(
            key: ValueKey('resize-${item.type.name}'),
            behavior: HitTestBehavior.opaque,
            onPanStart: (_) {
              _grabbed = item;
              _delta = Offset.zero;
            },
            onPanUpdate: (d) => _drag(d.delta, cell, resize: true),
            child: SizedBox(
              width: 36,
              height: 36,
              child: Icon(Icons.south_east_rounded, size: 18, color: scheme.primary),
            ),
          ),
        ),
      ],
    ]);
  }
}
