import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_audio.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_running_apps.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

/// "Running now": aplicativos abertos no PC, acima dos Favoritos na tela Apps.
/// Tocar no cartao traz o app para a frente; minimizar e fechar ficam nos botoes
/// (fechar sempre pergunta antes). O volume e o mudo vem do mixer, pela mesma identidade do app.
class RunningAppsSection extends StatelessWidget {
  const RunningAppsSection({super.key, required this.store, required this.audio});

  final RunningAppsStore store;
  final AudioStore audio;

  /// Largura minima de um cartao: na horizontal do Mate 20 Lite (~830 dp uteis) cabem dois.
  static const minCardWidth = 300.0;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final muted = scheme.onSurface.withValues(alpha: 0.6);
    return Watch((context) {
      final apps = store.apps.value;
      final loaded = store.loaded.value;
      return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 6, 0, 8),
          child: Row(children: [
            Icon(Icons.play_circle_outline_rounded, size: 14, color: scheme.primary),
            const SizedBox(width: 6),
            Text('RUNNING NOW',
                style: TextStyle(fontSize: 11, letterSpacing: 1.1, fontWeight: FontWeight.w600, color: muted)),
            if (apps.isNotEmpty)
              Text('  ${apps.length}',
                  style: TextStyle(fontSize: 11, fontWeight: FontWeight.w600, color: scheme.primary)),
          ]),
        ),
        if (!loaded && store.error.value == null)
          const Padding(padding: EdgeInsets.all(12), child: LinearProgressIndicator(minHeight: 2))
        else if (apps.isEmpty)
          Padding(
            padding: const EdgeInsets.fromLTRB(4, 0, 0, 8),
            child: Text(
              store.error.value != null ? 'Could not read the open applications.' : 'No open applications.',
              style: TextStyle(color: muted, fontSize: 13),
            ),
          )
        else
          LayoutBuilder(builder: (context, box) {
            const gap = 10.0;
            final columns = (box.maxWidth / minCardWidth).floor().clamp(1, 4);
            final width = (box.maxWidth - gap * (columns - 1)) / columns;
            return Wrap(spacing: gap, runSpacing: gap, children: [
              for (final a in apps)
                SizedBox(
                  width: width,
                  child: RunningAppCard(key: ValueKey(a.id), app: a, store: store, audio: audio),
                ),
            ]);
          }),
        const SizedBox(height: 14),
      ]);
    });
  }
}

class RunningAppCard extends StatefulWidget {
  const RunningAppCard({super.key, required this.app, required this.store, required this.audio});

  final KzRunningApp app;
  final RunningAppsStore store;
  final AudioStore audio;

  @override
  State<RunningAppCard> createState() => _RunningAppCardState();
}

class _RunningAppCardState extends State<RunningAppCard> {
  bool _expanded = false;
  bool _busy = false;

  KzRunningApp get app => widget.app;

  Future<void> _do(Future<RunningAppResult> Function() action) async {
    if (_busy) return;
    setState(() => _busy = true);
    final r = await action();
    if (!mounted) return;
    setState(() => _busy = false);
    if (r.message != null) kzToast(context, r.message!);
  }

  Future<void> _confirmClose({KzWindow? window}) async {
    final what = window == null ? app.name : '"${window.title}"';
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('Close $what?'),
        content: const Text('KZ Companion asks the app to close, like clicking the X. '
            'It may ask to save your work first.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: const Color(0xFFC62828), foregroundColor: Colors.white),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Close'),
          ),
        ],
      ),
    );
    if (ok != true || !mounted) return;
    await _do(() => widget.store.close(app, window: window));
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final muted = scheme.onSurface.withValues(alpha: 0.6);
    final locked = app.elevated;
    final sub = app.subtitle;
    return Material(
      color: scheme.surfaceContainer,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(14),
        side: BorderSide(color: app.foreground ? scheme.primary.withValues(alpha: 0.7) : Colors.transparent, width: 1.2),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(14),
        onTap: locked ? null : () => _do(() => widget.store.focus(app)),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(10, 9, 6, 6),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              _Icon(app: app, store: widget.store),
              const SizedBox(width: 10),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(app.name,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600)),
                  if (sub.isNotEmpty)
                    Text(sub, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 11.5, color: muted))
                  else if (locked)
                    Text('Runs as administrator', style: TextStyle(fontSize: 11.5, color: muted)),
                ]),
              ),
              if (app.minimized)
                Tooltip(message: 'Minimized', child: Icon(Icons.minimize_rounded, size: 16, color: muted)),
              if (locked)
                Padding(
                  padding: const EdgeInsets.only(left: 4),
                  child: Tooltip(
                    message: 'Runs as administrator: control it on the PC',
                    child: Icon(Icons.lock_outline_rounded, size: 16, color: muted),
                  ),
                ),
              if (app.windows.length > 1)
                IconButton(
                  visualDensity: VisualDensity.compact,
                  tooltip: _expanded ? 'Hide windows' : '${app.windows.length} windows',
                  onPressed: () => setState(() => _expanded = !_expanded),
                  icon: Badge(
                    label: Text('${app.windows.length}'),
                    child: Icon(_expanded ? Icons.expand_less_rounded : Icons.filter_none_rounded, size: 18),
                  ),
                ),
            ]),
            _Volume(app: app, audio: widget.audio),
            if (_expanded) ...[
              const Divider(height: 10),
              for (final w in app.windows)
                _WindowRow(
                  app: app,
                  window: w,
                  store: widget.store,
                  onClose: () => _confirmClose(window: w),
                  locked: locked,
                  run: _do,
                ),
            ],
            Row(mainAxisAlignment: MainAxisAlignment.end, children: [
              if (_busy)
                const Padding(
                  padding: EdgeInsets.only(right: 8),
                  child: SizedBox(width: 14, height: 14, child: CircularProgressIndicator(strokeWidth: 2)),
                ),
              TextButton.icon(
                style: TextButton.styleFrom(visualDensity: VisualDensity.compact, minimumSize: const Size(0, 34)),
                onPressed: locked ? null : () => _do(() => widget.store.focus(app)),
                icon: Icon(app.minimized ? Icons.open_in_full_rounded : Icons.open_in_new_rounded, size: 16),
                label: Text(app.minimized ? 'Restore' : 'Focus'),
              ),
              IconButton(
                visualDensity: VisualDensity.compact,
                tooltip: 'Minimize',
                onPressed: locked || app.minimized ? null : () => _do(() => widget.store.minimize(app)),
                icon: const Icon(Icons.minimize_rounded, size: 18),
              ),
              IconButton(
                visualDensity: VisualDensity.compact,
                tooltip: 'Close',
                onPressed: locked ? null : _confirmClose,
                icon: Icon(Icons.close_rounded, size: 18, color: locked ? null : const Color(0xFFEF5350)),
              ),
            ]),
          ]),
        ),
      ),
    );
  }
}

class _Icon extends StatelessWidget {
  const _Icon({required this.app, required this.store});
  final KzRunningApp app;
  final RunningAppsStore store;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final bytes = store.icons.value[app.id];
      return KzAppIcon(KzApp(id: app.id, name: app.name, group: '', favorite: false, icon: bytes), size: 40);
    });
  }
}

/// Volume e mudo do app, so quando ele tem sessao de audio (mesma identidade do .exe).
class _Volume extends StatelessWidget {
  const _Volume({required this.app, required this.audio});
  final KzRunningApp app;
  final AudioStore audio;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final sessions = audio.state.value?.sessions.where((s) => !s.system && s.groupKey == app.id).toList() ?? const [];
      if (sessions.isEmpty) return const SizedBox.shrink();
      final ids = [for (final s in sessions) s.id];
      final muted = sessions.every((s) => s.muted);
      final volume = sessions.map((s) => s.volume).reduce((a, b) => a > b ? a : b);
      return SizedBox(
        height: 38,
        child: Row(children: [
          IconButton(
            visualDensity: VisualDensity.compact,
            tooltip: muted ? 'Unmute' : 'Mute',
            onPressed: () => audio.toggleGroupMute(sessions),
            icon: Icon(muted ? Icons.volume_off_rounded : Icons.volume_up_rounded, size: 20),
          ),
          Expanded(
            child: SliderTheme(
              data: SliderTheme.of(context)
                  .copyWith(trackHeight: 3, overlayShape: const RoundSliderOverlayShape(overlayRadius: 14)),
              child: Slider(
                value: (muted ? 0 : volume).toDouble(),
                min: 0,
                max: 100,
                onChanged: (v) => audio.setGroupVolume(app.id, ids, v.round()),
                onChangeEnd: (v) => audio.setGroupVolume(app.id, ids, v.round(), commit: true),
              ),
            ),
          ),
          SizedBox(
            width: 34,
            child: Text('${muted ? 0 : volume}', textAlign: TextAlign.end, style: const TextStyle(fontSize: 12)),
          ),
        ]),
      );
    });
  }
}

class _WindowRow extends StatelessWidget {
  const _WindowRow({
    required this.app,
    required this.window,
    required this.store,
    required this.onClose,
    required this.locked,
    required this.run,
  });

  final KzRunningApp app;
  final KzWindow window;
  final RunningAppsStore store;
  final VoidCallback onClose;
  final bool locked;
  final Future<void> Function(Future<RunningAppResult> Function()) run;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return InkWell(
      onTap: locked ? null : () => run(() => store.focus(app, window: window)),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 2),
        child: Row(children: [
          Icon(window.minimized ? Icons.minimize_rounded : Icons.crop_square_rounded,
              size: 14, color: window.foreground ? scheme.primary : scheme.onSurface.withValues(alpha: 0.5)),
          const SizedBox(width: 8),
          Expanded(
            child: Text(window.title, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12.5)),
          ),
          IconButton(
            visualDensity: VisualDensity.compact,
            iconSize: 16,
            tooltip: 'Minimize window',
            onPressed: locked || window.minimized ? null : () => run(() => store.minimize(app, window: window)),
            icon: const Icon(Icons.minimize_rounded),
          ),
          IconButton(
            visualDensity: VisualDensity.compact,
            iconSize: 16,
            tooltip: 'Close window',
            onPressed: locked ? null : onClose,
            icon: const Icon(Icons.close_rounded),
          ),
        ]),
      ),
    );
  }
}
