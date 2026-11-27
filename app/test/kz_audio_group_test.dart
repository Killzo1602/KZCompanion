import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_audio.dart';
import 'package:droiddeck/src/kz/kz_audio_widgets.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';

class _Recorder implements HttpClientAdapter {
  final calls = <String>[];

  @override
  void close({bool force = false}) {}

  @override
  Future<ResponseBody> fetch(RequestOptions o, Stream<Uint8List>? s, Future<void>? c) async {
    if (o.method != 'GET') calls.add('${o.method} ${o.path} ${jsonEncode(o.data)}');
    return ResponseBody.fromString(
      jsonEncode({
        'outputName': 'Speakers',
        'volume': 50,
        'muted': false,
        'inputMuted': false,
        'deafened': false,
        'sessions': [
          {'id': 'd1', 'groupKey': 'disc', 'name': 'Discord', 'volume': 100, 'muted': false, 'active': true, 'system': false},
          {'id': 'd2', 'groupKey': 'disc', 'name': 'Discord', 'volume': 70, 'muted': false, 'active': false, 'system': false},
          {'id': 'f1', 'groupKey': 'ff', 'name': 'Mozilla Firefox', 'volume': 60, 'muted': false, 'active': false, 'system': false},
        ],
      }),
      200,
      headers: {Headers.contentTypeHeader: ['application/json']},
    );
  }
}

void main() {
  test('AudioGroup groups by groupKey, never by name', () {
    AudioSession s(String id, String key, String name) =>
        AudioSession(id: id, name: name, volume: 50, muted: false, active: true, system: false, groupKey: key);
    final groups = AudioGroup.from([s('1', 'a', 'Discord'), s('2', 'b', 'Discord PTB'), s('3', 'a', 'Discord'), s('4', 'c', 'Discord')]);
    expect(groups.length, 3);
    expect(groups.first.members.map((m) => m.id), ['1', '3']);
    expect(groups.where((g) => g.key == 'c').single.members.single.id, '4');
  });

  testWidgets('grouped mixer: one row, primary controls hit every session, sessions stay independent', (tester) async {
    tester.view.physicalSize = const Size(1200, 900);
    tester.view.devicePixelRatio = 2;
    addTearDown(tester.view.reset);

    final rec = _Recorder();
    final client = DroidDeckClient(baseUrl: 'http://x', dio: Dio()..httpClientAdapter = rec);
    final signalR = SignalRService()..connectionStatus.value = 'Connected';
    final store = AudioStore(client: client, signalR: signalR);
    await tester.runAsync(store.refresh);

    Future<void> settle() async {
      for (var i = 0; i < 6; i++) {
        await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 40)));
        await tester.pump(const Duration(milliseconds: 20));
      }
    }

    final key = GlobalKey();
    await tester.pumpWidget(RepaintBoundary(
      key: key,
      child: MaterialApp(
        theme: buildAppTheme(const ThemeSettings(), Brightness.dark),
        home: Scaffold(body: Padding(padding: const EdgeInsets.all(12), child: SizedBox(width: 420, child: KzMixer(store)))),
      ),
    ));
    await tester.pump();

    expect(find.text('Discord'), findsOneWidget);
    expect(find.text('2'), findsOneWidget);
    expect(find.text('Mozilla Firefox'), findsOneWidget);
    expect(find.text('70-100%'), findsOneWidget);
    expect(rec.calls, isEmpty, reason: 'exibir o grupo nao pode mandar comando algum');

    await tester.drag(find.byType(Slider).first, const Offset(-80, 0));
    await settle();
    final volCall = rec.calls.lastWhere((c) => c.contains('/api/audio/sessions '));
    expect(volCall, contains('"ids":["d1","d2"]'));
    expect(volCall, contains('"volume":'));
    expect(volCall, isNot(contains('f1')), reason: 'Firefox nao pode ser tocado');

    rec.calls.clear();
    await tester.tap(find.byTooltip('Mute all'));
    await settle();
    expect(rec.calls.single, contains('"ids":["d1","d2"]'));
    expect(rec.calls.single, contains('"mute":true'));
    await tester.pump(const Duration(seconds: 1));

    await tester.tap(find.text('Discord'));
    await tester.pump();
    expect(find.text('Session 1'), findsOneWidget);
    expect(find.textContaining('Session 2'), findsOneWidget);
    rec.calls.clear();
    await tester.tap(find.byTooltip('Unmute').first);
    await settle();
    expect(rec.calls.single, startsWith('PUT /api/audio/sessions/d1 '));

    await tester.runAsync(() async {
      final b = key.currentContext!.findRenderObject() as RenderRepaintBoundary;
      final img = await b.toImage(pixelRatio: 1);
      final bytes = (await img.toByteData(format: ui.ImageByteFormat.png))!;
      final dir = Directory('build/layout')..createSync(recursive: true);
      File('${dir.path}/mixer_group_expanded.png').writeAsBytesSync(Uint8List.view(bytes.buffer));
    });
    await tester.pump(const Duration(seconds: 1));
    store.dispose();
  });
}
