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

/// Volume geral do Windows (saída padrão) com mudo.
class KzVolume extends StatefulWidget {
  const KzVolume(this.c, {super.key});
  final KzController c;

  @override
  State<KzVolume> createState() => _KzVolumeState();
}

class _KzVolumeState extends State<KzVolume> {
  double? _drag;

  @override
  Widget build(BuildContext context) {
    final c = widget.c;
    return Watch((context) {
      c.home.outputs.value;
      final out = c.mainOutput;
      if (out == null) {
        return Text('Volume unavailable',
            style: TextStyle(color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.5)));
      }
      final id = out.device.id ?? out.id ?? '';
      final volume = (_drag ?? out.device.volume.toDouble()).clamp(0, 100).toDouble();
      final muted = out.device.mute;
      return Row(children: [
        IconButton(
          onPressed: () => c.home.toggleMute(id, muted),
          icon: Icon(muted
              ? Icons.volume_off_rounded
              : volume < 1
                  ? Icons.volume_mute_rounded
                  : Icons.volume_up_rounded),
        ),
        Expanded(
          child: Slider(
            value: volume,
            min: 0,
            max: 100,
            onChanged: (v) => setState(() => _drag = v),
            onChangeEnd: (v) async {
              await c.home.setVolume(id, v.round());
              if (mounted) setState(() => _drag = null);
            },
          ),
        ),
        SizedBox(width: 38, child: Text('${volume.round()}%', textAlign: TextAlign.end)),
      ]);
    });
  }
}
