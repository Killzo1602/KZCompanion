import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_audio.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

const _red = Color(0xFFEF5350);

SliderThemeData _sliderTheme(BuildContext context) => SliderTheme.of(context).copyWith(
      trackHeight: 4,
      thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 9),
      overlayShape: const RoundSliderOverlayShape(overlayRadius: 18),
    );

/// Mudo | Deafen | Volume do Windows. Tudo reflete o estado real do PC; offline fica esmaecido.
class KzAudioControls extends StatelessWidget {
  const KzAudioControls(this.store, {super.key});
  final AudioStore store;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return KzCard(
      title: 'Audio controls',
      icon: Icons.volume_up_rounded,
      child: Watch((context) {
        final s = store.state.value;
        final online = store.signalR.linkState.value == 'online';
        final enabled = online && s != null && s.volume != null;
        final volume = s?.volume;
        final muted = s?.muted ?? false;
        return Opacity(
          opacity: enabled ? 1 : 0.45,
          child: IgnorePointer(
            ignoring: !enabled,
            child: Row(children: [
              _toggle(
                context,
                icon: muted ? Icons.volume_off_rounded : Icons.volume_up_rounded,
                label: muted ? 'Muted' : 'Mute',
                active: muted,
                color: _red,
                onTap: store.toggleMasterMute,
              ),
              const SizedBox(width: 8),
              _toggle(
                context,
                icon: Icons.headset_off_rounded,
                label: 'Deafen',
                active: s?.deafened ?? false,
                color: _red,
                onTap: store.toggleDeafen,
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Row(children: [
                    Expanded(
                      child: Text(
                        s == null || s.outputName.isEmpty ? 'Windows volume' : s.outputName,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 12, color: scheme.onSurface.withValues(alpha: 0.65)),
                      ),
                    ),
                    Text(volume == null ? '--' : '$volume%',
                        style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
                  ]),
                  SliderTheme(
                    data: _sliderTheme(context),
                    child: Slider(
                      value: (volume ?? 0).toDouble(),
                      min: 0,
                      max: 100,
                      onChanged: (v) => store.setMasterVolume(v.round()),
                      onChangeEnd: (v) => store.setMasterVolume(v.round(), commit: true),
                    ),
                  ),
                ]),
              ),
            ]),
          ),
        );
      }),
    );
  }

  Widget _toggle(BuildContext context,
      {required IconData icon,
      required String label,
      required bool active,
      required Color color,
      required VoidCallback onTap}) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      color: active ? color.withValues(alpha: 0.18) : scheme.surfaceContainer,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: onTap,
        child: Container(
          width: 76,
          height: 58,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: active ? color : Colors.transparent, width: 1.2),
          ),
          child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
            Icon(icon, size: 22, color: active ? color : scheme.onSurface),
            const SizedBox(height: 3),
            Text(label, style: TextStyle(fontSize: 11, color: active ? color : null, fontWeight: FontWeight.w600)),
          ]),
        ),
      ),
    );
  }
}

/// Mixer de volume ao estilo do Windows 11: uma linha por sessao de audio, cada uma independente.
class KzMixer extends StatelessWidget {
  const KzMixer(this.store, {super.key});
  final AudioStore store;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return KzCard(
      title: 'Volume mixer',
      icon: Icons.tune_rounded,
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 6),
      child: Watch((context) {
        final s = store.state.value;
        store.icons.value; // icones chegam depois do estado
        final online = store.signalR.linkState.value == 'online';
        if (s == null) {
          return Padding(
            padding: const EdgeInsets.symmetric(vertical: 14),
            child: Center(
              child: Text(online ? 'Loading audio sessions…' : 'PC offline',
                  style: TextStyle(color: scheme.onSurface.withValues(alpha: 0.55))),
            ),
          );
        }
        if (s.sessions.isEmpty) {
          return Padding(
            padding: const EdgeInsets.symmetric(vertical: 14),
            child: Center(
              child: Text('No applications are using audio right now.',
                  style: TextStyle(color: scheme.onSurface.withValues(alpha: 0.55))),
            ),
          );
        }
        return Opacity(
          opacity: online ? 1 : 0.45,
          child: IgnorePointer(
            ignoring: !online,
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              for (final session in s.sessions) _SessionRow(key: ValueKey(session.id), store: store, session: session),
            ]),
          ),
        );
      }),
    );
  }
}

class _SessionRow extends StatelessWidget {
  const _SessionRow({super.key, required this.store, required this.session});
  final AudioStore store;
  final AudioSession session;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final icon = store.icons.value[session.id];
    final dim = !session.active && !session.muted;
    return SizedBox(
      height: 50,
      child: Row(children: [
        SizedBox(
          width: 28,
          height: 28,
          child: icon != null
              ? Image.memory(icon, filterQuality: FilterQuality.medium, gaplessPlayback: true)
              : Icon(session.system ? Icons.notifications_active_outlined : Icons.apps_rounded,
                  size: 24, color: scheme.primary),
        ),
        const SizedBox(width: 10),
        SizedBox(
          width: 150,
          child: Text(session.name,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 13, color: dim ? scheme.onSurface.withValues(alpha: 0.6) : null)),
        ),
        Expanded(
          child: SliderTheme(
            data: _sliderTheme(context),
            child: Slider(
              value: session.volume.toDouble(),
              min: 0,
              max: 100,
              onChanged: (v) => store.setSessionVolume(session.id, v.round()),
              onChangeEnd: (v) => store.setSessionVolume(session.id, v.round(), commit: true),
            ),
          ),
        ),
        SizedBox(
          width: 40,
          child: Text('${session.volume}%',
              textAlign: TextAlign.end,
              style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: session.muted ? _red : null)),
        ),
        IconButton(
          visualDensity: VisualDensity.compact,
          tooltip: session.muted ? 'Unmute' : 'Mute',
          onPressed: () => store.toggleSessionMute(session.id),
          icon: Icon(session.muted ? Icons.volume_off_rounded : Icons.volume_up_rounded,
              size: 22, color: session.muted ? _red : scheme.onSurface.withValues(alpha: 0.8)),
        ),
      ]),
    );
  }
}
