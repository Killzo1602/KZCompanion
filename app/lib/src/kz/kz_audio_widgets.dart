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
          final speakersMuted = s?.muted ?? false;
          final micMuted = s?.inputMuted;
          return Opacity(
            opacity: enabled ? 1 : 0.45,
            child: IgnorePointer(
              ignoring: !enabled,
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                Row(children: [
                  Expanded(
                    // Mute = MICROFONE do Windows (so ele). Mostra o estado real do microfone.
                    child: _toggle(context,
                        icon: micMuted == true ? Icons.mic_off_rounded : Icons.mic_rounded,
                        label: micMuted == null ? 'No mic' : (micMuted ? 'Mic muted' : 'Mute mic'),
                        active: micMuted == true,
                        onTap: micMuted == null ? () {} : store.toggleMicMute),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: _toggle(context,
                        icon: Icons.headset_off_rounded,
                        label: (s?.deafened ?? false) ? 'Deafened' : 'Deafen',
                        active: s?.deafened ?? false,
                        onTap: store.toggleDeafen),
                  ),
                ]),
                const SizedBox(height: 4),
                Row(children: [
                  if (speakersMuted)
                    const Padding(
                      padding: EdgeInsets.only(right: 4),
                      child: Icon(Icons.volume_off_rounded, size: 15, color: _red),
                    ),
                  Expanded(
                    child: Text(
                      s == null || s.outputName.isEmpty ? 'Windows volume' : (speakersMuted ? '${s.outputName} (muted)' : s.outputName),
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

/// Sessoes do mesmo aplicativo (mesma identidade de executavel no PC). Cada sessao continua com o
/// seu id; o grupo e so visual e para o comando "todas de uma vez".
class AudioGroup {
  AudioGroup(this.key, this.members);
  final String key;
  final List<AudioSession> members;

  AudioSession get first => members.first;
  String get name => first.name;
  bool get system => first.system;
  bool get active => members.any((m) => m.active);
  int get maxVolume => members.map((m) => m.volume).reduce((a, b) => a > b ? a : b);
  int get minVolume => members.map((m) => m.volume).reduce((a, b) => a < b ? a : b);
  bool get allMuted => members.every((m) => m.muted);
  bool get someMuted => members.any((m) => m.muted);

  static List<AudioGroup> from(List<AudioSession> sessions) {
    final map = <String, List<AudioSession>>{};
    for (final s in sessions) {
      map.putIfAbsent(s.groupKey, () => []).add(s);
    }
    return [for (final e in map.entries) AudioGroup(e.key, e.value)];
  }
}

/// Mixer de volume ao estilo do Windows 11: uma linha por aplicativo. Aplicativo com varias sessoes
/// (Discord, navegadores...) mostra um slider/mudo principal e abre as sessoes, cada uma independente.
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
        final open = store.expanded.value;
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
        final groups = AudioGroup.from(s.sessions);
        final rows = <Widget>[];
        for (final g in groups) {
          if (g.members.length == 1) {
            rows.add(_SessionRow(key: ValueKey(g.first.id), store: store, session: g.first, label: g.name));
          } else {
            rows.add(_GroupRow(key: ValueKey('g${g.key}'), store: store, group: g, open: open.contains(g.key)));
          }
        }
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

Widget _appIcon(BuildContext context, AudioStore store, AudioSession session, {double size = 26}) {
  final scheme = Theme.of(context).colorScheme;
  final icon = store.icons.value[session.id];
  return SizedBox(
    width: size,
    height: size,
    // contain: o icone inteiro, sem cortar, qualquer que seja a proporcao
    child: icon != null
        ? Image.memory(icon, fit: BoxFit.contain, filterQuality: FilterQuality.medium, gaplessPlayback: true)
        : Icon(session.system ? Icons.notifications_active_outlined : Icons.apps_rounded,
            size: size - 4, color: scheme.primary),
  );
}

/// Um aplicativo com varias sessoes: slider e mudo principais + lista de sessoes que abre/fecha.
class _GroupRow extends StatelessWidget {
  const _GroupRow({super.key, required this.store, required this.group, required this.open});
  final AudioStore store;
  final AudioGroup group;
  final bool open;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final ids = [for (final m in group.members) m.id];
    final mixedLevels = group.minVolume != group.maxVolume;
    final mixedMute = group.someMuted && !group.allMuted;
    final dim = !group.active && !group.someMuted;
    final muteColor = group.allMuted ? _red : (mixedMute ? const Color(0xFFFBBF24) : scheme.onSurface.withValues(alpha: 0.8));
    return Padding(
      padding: const EdgeInsets.only(bottom: 4),
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        InkWell(
          borderRadius: BorderRadius.circular(8),
          onTap: () => store.toggleExpanded(group.key),
          child: Row(children: [
            _appIcon(context, store, group.first),
            const SizedBox(width: 8),
            Flexible(
              child: Text(group.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 13, color: dim ? scheme.onSurface.withValues(alpha: 0.6) : null)),
            ),
            const SizedBox(width: 6),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
              decoration: BoxDecoration(
                color: scheme.primary.withValues(alpha: 0.16),
                borderRadius: BorderRadius.circular(8),
              ),
              child: Text('${group.members.length}',
                  style: TextStyle(fontSize: 10.5, fontWeight: FontWeight.w700, color: scheme.primary)),
            ),
            Icon(open ? Icons.expand_less_rounded : Icons.expand_more_rounded, size: 20,
                color: scheme.onSurface.withValues(alpha: 0.6)),
            const Spacer(),
            Text(mixedLevels ? '${group.minVolume}-${group.maxVolume}%' : '${group.maxVolume}%',
                style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: group.allMuted ? _red : null)),
            IconButton(
              visualDensity: VisualDensity.compact,
              tooltip: group.allMuted ? 'Unmute all' : (mixedMute ? 'Some muted: mute all' : 'Mute all'),
              onPressed: () => store.toggleGroupMute(group.members),
              icon: Icon(
                group.allMuted
                    ? Icons.volume_off_rounded
                    : (mixedMute ? Icons.volume_down_rounded : Icons.volume_up_rounded),
                size: 22,
                color: muteColor,
              ),
            ),
          ]),
        ),
        // O slider principal mostra o MAIOR volume e so muda as sessoes quando o dedo o move.
        SizedBox(
          height: 30,
          child: SliderTheme(
            data: _sliderTheme(context),
            child: Slider(
              value: group.maxVolume.toDouble(),
              min: 0,
              max: 100,
              onChanged: (v) => store.setGroupVolume(group.key, ids, v.round()),
              onChangeEnd: (v) => store.setGroupVolume(group.key, ids, v.round(), commit: true),
            ),
          ),
        ),
        if (open)
          Container(
            margin: const EdgeInsets.only(left: 12, bottom: 4),
            padding: const EdgeInsets.only(left: 8),
            decoration: BoxDecoration(
              border: Border(left: BorderSide(color: scheme.primary.withValues(alpha: 0.35), width: 2)),
            ),
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              for (var i = 0; i < group.members.length; i++)
                _SessionRow(
                  key: ValueKey(group.members[i].id),
                  store: store,
                  session: group.members[i],
                  label: 'Session ${i + 1}${group.members[i].active ? '' : ' (idle)'}',
                  showIcon: false,
                ),
            ]),
          ),
      ]),
    );
  }
}

/// Duas linhas: icone + nome + % + mudo, e o slider abaixo (largo, bom para o dedo).
class _SessionRow extends StatelessWidget {
  const _SessionRow(
      {super.key, required this.store, required this.session, required this.label, this.showIcon = true});
  final AudioStore store;
  final AudioSession session;
  final String label;
  final bool showIcon;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final dim = !session.active && !session.muted;
    return Padding(
      padding: const EdgeInsets.only(bottom: 4),
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        Row(children: [
          if (showIcon) ...[
            _appIcon(context, store, session),
            const SizedBox(width: 8),
          ],
          Expanded(
            child: Text(label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: showIcon ? 13 : 12, color: dim ? scheme.onSurface.withValues(alpha: 0.6) : null)),
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
