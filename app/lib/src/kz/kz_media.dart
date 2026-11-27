import 'dart:convert';
import 'dart:typed_data';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:droiddeck/src/kz/kz_style.dart';
import 'package:flutter/material.dart';

class KzNowPlaying extends StatelessWidget {
  const KzNowPlaying(this.c, {super.key});
  final KzController c;

  @override
  Widget build(BuildContext context) {
    final muted = Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6);
    return KzCard(
      expand: true,
      accent: KzColors.media,
      padding: const EdgeInsets.all(12),
      child: SizedBox.expand(
        child: LayoutBuilder(builder: (context, box) {
          final cover = (box.maxHeight - 4).clamp(0.0, box.maxWidth * 0.36).clamp(0.0, 150.0);
          return Row(children: [
            if (cover >= 48) ...[
              Watch((context) {
                c.home.mediaSessions.value;
                return _Cover(c.mediaSession?.thumbnailBase64, size: cover);
              }),
              const SizedBox(width: 14),
            ],
            Expanded(
              child: FittedBox(
                fit: BoxFit.scaleDown,
                alignment: Alignment.centerLeft,
                child: SizedBox(
                  width: (box.maxWidth - (cover >= 48 ? cover + 14 : 0)).clamp(200.0, 520.0),
                  child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                    Watch((context) {
                      c.home.mediaSessions.value;
                      final s = c.mediaSession;
                      final playing = s?.playbackStatus == 'Playing';
                      final has = s != null;
                      final title = has && (s.title ?? '').isNotEmpty ? s.title! : 'Nothing playing';
                      return Column(mainAxisSize: MainAxisSize.min, children: [
                        Text(title,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: has ? null : muted)),
                        Text(has ? (s.artist ?? '') : '',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(fontSize: 12.5, color: muted)),
                        const SizedBox(height: 6),
                        Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                          IconButton(
                              iconSize: 28,
                              onPressed: has ? () => c.media('previous') : null,
                              icon: const Icon(Icons.skip_previous_rounded)),
                          const SizedBox(width: 12),
                          IconButton.filled(
                            iconSize: 30,
                            style: IconButton.styleFrom(
                                backgroundColor: Theme.of(context).colorScheme.onSurface,
                                foregroundColor: Theme.of(context).colorScheme.surface),
                            onPressed: has ? () => c.media('playpause') : null,
                            icon: Icon(playing ? Icons.pause_rounded : Icons.play_arrow_rounded),
                          ),
                          const SizedBox(width: 12),
                          IconButton(
                              iconSize: 28,
                              onPressed: has ? () => c.media('next') : null,
                              icon: const Icon(Icons.skip_next_rounded)),
                        ]),
                      ]);
                    }),
                    KzVolume(c),
                  ]),
                ),
              ),
            ),
          ]);
        }),
      ),
    );
  }
}

class _Cover extends StatefulWidget {
  const _Cover(this.data, {required this.size});
  final String? data;
  final double size;

  @override
  State<_Cover> createState() => _CoverState();
}

class _CoverState extends State<_Cover> {
  Uint8List? _bytes;

  @override
  void initState() {
    super.initState();
    _decode();
  }

  @override
  void didUpdateWidget(_Cover old) {
    super.didUpdateWidget(old);
    if (old.data != widget.data) _decode();
  }

  void _decode() {
    try {
      final data = widget.data;
      _bytes = data == null ? null : base64Decode(data.substring(data.indexOf(',') + 1));
    } on FormatException {
      _bytes = null;
    }
  }

  @override
  Widget build(BuildContext context) {
    final empty = Container(
      decoration: const BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0x66D946EF), Color(0x668B5CF6)],
        ),
      ),
      child: Icon(Icons.music_note_rounded, size: widget.size * 0.45, color: Colors.white70),
    );
    final bytes = _bytes;
    return ClipRRect(
      borderRadius: BorderRadius.circular(14),
      child: SizedBox(
        width: widget.size,
        height: widget.size,
        child: bytes == null || bytes.isEmpty
            ? empty
            : Image.memory(bytes, fit: BoxFit.cover, gaplessPlayback: true, errorBuilder: (_, __, ___) => empty),
      ),
    );
  }
}

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
              child: SliderTheme(
                data: kzSliderTheme(context, color: KzColors.media),
                child: Slider(
                  value: volume.toDouble(),
                  min: 0,
                  max: 100,
                  onChanged: (v) => c.audio.setMasterVolume(v.round()),
                  onChangeEnd: (v) => c.audio.setMasterVolume(v.round(), commit: true),
                ),
              ),
            ),
            SizedBox(width: 38, child: Text('$volume%', textAlign: TextAlign.end)),
          ]),
        ),
      );
    });
  }
}
