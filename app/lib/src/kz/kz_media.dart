import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:flutter/material.dart';

/// Faixa atual + anterior / play-pause / próxima.
class KzMediaControls extends StatelessWidget {
  const KzMediaControls(this.c, {super.key, this.compact = false});
  final KzController c;
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Watch((context) {
      c.home.mediaSessions.value;
      final s = c.mediaSession;
      final playing = s?.playbackStatus == 'Playing';
      final has = s != null;
      final title = has && (s.title ?? '').isNotEmpty ? s.title! : 'Nothing playing';
      final artist = has ? (s.artist ?? '') : '';
      return Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(title,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                  fontSize: compact ? 13 : 15,
                  fontWeight: FontWeight.w600,
                  color: has ? null : scheme.onSurface.withValues(alpha: 0.5))),
          if (artist.isNotEmpty)
            Text(artist,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 12, color: scheme.onSurface.withValues(alpha: 0.65))),
          SizedBox(height: compact ? 6 : 12),
          Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            _btn(Icons.skip_previous_rounded, has ? () => c.media('previous') : null, scheme),
            const SizedBox(width: 8),
            IconButton.filled(
              iconSize: 30,
              onPressed: has ? () => c.media('playpause') : null,
              icon: Icon(playing ? Icons.pause_rounded : Icons.play_arrow_rounded),
            ),
            const SizedBox(width: 8),
            _btn(Icons.skip_next_rounded, has ? () => c.media('next') : null, scheme),
          ]),
        ],
      );
    });
  }

  Widget _btn(IconData icon, VoidCallback? onTap, ColorScheme scheme) => IconButton(
        iconSize: 28,
        onPressed: onTap,
        icon: Icon(icon),
      );
}

/// Volume geral do Windows (saida padrao REAL, vinda do PC) com mudo.
class KzVolume extends StatelessWidget {
  const KzVolume(this.c, {super.key});
  final KzController c;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final s = c.audio.state.value;
      final online = c.audio.signalR.linkState.value == 'online';
      final volume = s?.volume;
      if (volume == null) {
        return Text(online ? 'Volume unavailable' : 'PC offline',
            style: TextStyle(color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.5)));
      }
      final muted = s!.muted;
      return Opacity(
        opacity: online ? 1 : 0.45,
        child: IgnorePointer(
          ignoring: !online,
          child: Row(children: [
            IconButton(
              onPressed: c.audio.toggleMasterMute,
              icon: Icon(muted ? Icons.volume_off_rounded : Icons.volume_up_rounded),
            ),
            Expanded(
              child: Slider(
                value: volume.toDouble(),
                min: 0,
                max: 100,
                onChanged: (v) => c.audio.setMasterVolume(v.round()),
                onChangeEnd: (v) => c.audio.setMasterVolume(v.round(), commit: true),
              ),
            ),
            SizedBox(width: 38, child: Text('$volume%', textAlign: TextAlign.end)),
          ]),
        ),
      );
    });
  }
}
