import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_audio.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

const _red = Color(0xFFEF5350);

SliderThemeData _sliderTheme(BuildContext context) => SliderTheme.of(context).copyWith(
      trackHeight: 4,
      thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 10),
      overlayShape: const RoundSliderOverlayShape(overlayRadius: 20),
    );

/// Avisos do audio: PC com app antigo, falha de comando. Vazio quando esta tudo bem.
class _AudioNotice extends StatelessWidget {
  const _AudioNotice(this.store);
  final AudioStore store;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final scheme = Theme.of(context).colorScheme;
      String? text;
      if (store.supported.value == false) {
        text = 'The KZ Companion on your PC is outdated and has no audio support. Close it from the tray (Encerrar) and start the new version.';
      } else if (store.lastError.value != null && store.signalR.linkState.value == 'online') {
        text = store.lastError.value;
      }
      if (text == null) return const SizedBox.shrink();
      return Padding(
        padding: const EdgeInsets.only(bottom: 8),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Icon(Icons.info_outline_rounded, size: 16, color: Color(0xFFFBBF24)),
          const SizedBox(width: 6),
          Expanded(child: Text(text, style: TextStyle(fontSize: 11.5, color: scheme.onSurface.withValues(alpha: 0.8)))),
        ]),
      );
    });
  }
}

/// Mudo | Deafen e o volume do Windows. Tudo reflete o estado real do PC; offline fica esmaecido.
class KzAudioControls extends StatelessWidget {
  const KzAudioControls(this.store, {super.key});
  final AudioStore store;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return KzCard(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 4),
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        _AudioNotice(store),
        Watch((context) {
          final s = store.state.value;
          final online = store.signalR.linkState.value == 'online';
          final enabled = online && s != null && s.volume != null;
          final volume = s?.volume;
          final muted = s?.muted ?? false;
          return Opacity(
            opacity: enabled ? 1 : 0.45,
            child: IgnorePointer(
              ignoring: !enabled,
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                Row(children: [
                  Expanded(
                    child: _toggle(context,
                        icon: muted ? Icons.volume_off_rounded : Icons.volume_up_rounded,
                        label: muted ? 'Muted' : 'Mute',
                        active: muted,
                        onTap: store.toggleMasterMute),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: _toggle(context,
                        icon: Icons.headset_off_rounded,
                        label: 'Deafen',
                        active: s?.deafened ?? false,
                        onTap: store.toggleDeafen),
                  ),
                ]),
                const SizedBox(height: 4),
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
          );
        }),
      ]),
    );
  }

  Widget _toggle(BuildContext context,
      {required IconData icon, required String label, required bool active, required VoidCallback onTap}) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      color: active ? _red.withValues(alpha: 0.18) : scheme.surfaceContainer,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: onTap,
        child: Container(
          height: 44,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: active ? _red : Colors.transparent, width: 1.2),
          ),
          child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            Icon(icon, size: 20, color: active ? _red : scheme.onSurface),
            const SizedBox(width: 6),
            Text(label, style: TextStyle(fontSize: 13, color: active ? _red : null, fontWeight: FontWeight.w600)),
          ]),
        ),
      ),
    );
  }
}

/// Mixer de volume ao estilo do Windows 11: uma linha por sessao de audio, cada uma independente.
/// [scroll] = rola sozinho dentro do espaco que recebe (coluna da direita do painel).
class KzMixer extends StatelessWidget {
  const KzMixer(this.store, {super.key, this.scroll = false});
  final AudioStore store;
  final bool scroll;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return KzCard(
      title: 'Volume mixer',
      icon: Icons.tune_rounded,
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 6),
      expand: scroll,
      child: Watch((context) {
        final s = store.state.value;
        store.icons.value; // icones chegam depois do estado
        final online = store.signalR.linkState.value == 'online';
        final muted = scheme.onSurface.withValues(alpha: 0.55);
        if (s == null) {
          return Padding(
            padding: const EdgeInsets.symmetric(vertical: 14),
            child: Center(
              child: Text(
                store.supported.value == false
                    ? 'Audio is not available (PC app outdated)'
                    : online
                        ? 'Loading audio sessions…'
                        : 'PC offline',
                textAlign: TextAlign.center,
                style: TextStyle(color: muted),
              ),
            ),
          );
        }
        if (s.sessions.isEmpty) {
          return Padding(
            padding: const EdgeInsets.symmetric(vertical: 14),
            child: Center(
              child: Text('No applications are using audio right now.', style: TextStyle(color: muted)),
            ),
          );
        }
        final rows = [
          for (final session in s.sessions) _SessionRow(key: ValueKey(session.id), store: store, session: session),
        ];
        final list = scroll
            ? ListView(shrinkWrap: true, padding: EdgeInsets.zero, children: rows)
            : Column(mainAxisSize: MainAxisSize.min, children: rows);
        return Opacity(
          opacity: online ? 1 : 0.45,
          child: IgnorePointer(ignoring: !online, child: list),
        );
      }),
    );
  }
}

/// Duas linhas: icone + nome + % + mudo, e o slider abaixo (largo, bom para o dedo).
class _SessionRow extends StatelessWidget {
  const _SessionRow({super.key, required this.store, required this.session});
  final AudioStore store;
  final AudioSession session;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final icon = store.icons.value[session.id];
    final dim = !session.active && !session.muted;
    return Padding(
      padding: const EdgeInsets.only(bottom: 4),
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        Row(children: [
          SizedBox(
            width: 26,
            height: 26,
            child: icon != null
                ? Image.memory(icon, filterQuality: FilterQuality.medium, gaplessPlayback: true)
                : Icon(session.system ? Icons.notifications_active_outlined : Icons.apps_rounded,
                    size: 22, color: scheme.primary),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Text(session.name,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 13, color: dim ? scheme.onSurface.withValues(alpha: 0.6) : null)),
          ),
          Text('${session.volume}%',
              style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: session.muted ? _red : null)),
          IconButton(
            visualDensity: VisualDensity.compact,
            tooltip: session.muted ? 'Unmute' : 'Mute',
            onPressed: () => store.toggleSessionMute(session.id),
            icon: Icon(session.muted ? Icons.volume_off_rounded : Icons.volume_up_rounded,
                size: 22, color: session.muted ? _red : scheme.onSurface.withValues(alpha: 0.8)),
          ),
        ]),
        SizedBox(
          height: 30,
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
      ]),
    );
  }
}
