import 'dart:convert';
import 'dart:typed_data';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/dev_page.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_sessions.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

class _Pc implements HttpClientAdapter {
  final posts = <String>[];

  @override
  void close({bool force = false}) {}

  @override
  Future<ResponseBody> fetch(RequestOptions o, Stream<Uint8List>? s, Future<void>? c) async {
    Object body = [];
    if (o.method == 'POST') {
      posts.add(o.path);
      body = {'ok': true};
    } else if (o.path == '/api/dev/github') {
      body = {
        'installed': true,
        'error': null,
        'repos': [
          {'repo': 'a-repository-with-a-very-long-name', 'branch': 'main', 'state': 'failure', 'title': 'fix: thing'}
        ],
        'pullRequests': [
          {'repo': 'app', 'branch': '#7', 'state': 'pending', 'title': 'Add thing'}
        ],
      };
    } else if (o.path == '/api/dev/docker') {
      body = {
        'installed': true,
        'error': null,
        'containers': [
          {'id': 'aaaaaaaaaaaa', 'name': 'web', 'image': 'nginx', 'running': true, 'status': 'Up 2 hours'},
          {'id': 'bbbbbbbbbbbb', 'name': 'db', 'image': 'postgres', 'running': false, 'status': 'Exited'},
        ],
      };
    }
    return ResponseBody.fromString(jsonEncode(body), 200, headers: {
      Headers.contentTypeHeader: ['application/json'],
    });
  }
}

KzController _controller(_Pc pc) => KzController(
    client: DroidDeckClient(baseUrl: 'http://127.0.0.1:1', dio: Dio()..httpClientAdapter = pc),
    signalR: SignalRService()..connectionStatus.value = 'Connected');

Future<void> _settle(WidgetTester tester) async {
  for (var i = 0; i < 6; i++) {
    await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 5)));
    await tester.pump(const Duration(milliseconds: 100));
  }
}

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  testWidgets('GitHub and Docker fit a small phone; stop asks first, start does not', (tester) async {
    tester.view.physicalSize = const Size(640, 320);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final pc = _Pc();
    final c = _controller(pc);
    await tester.pumpWidget(MaterialApp(
      theme: buildAppTheme(const ThemeSettings(), Brightness.dark),
      home: Scaffold(body: DevPage(c: c)),
    ));
    await _settle(tester);
    expect(tester.takeException(), isNull);
    expect(find.text('app #7'), findsOneWidget);
    expect(find.text('web'), findsOneWidget);

    await tester.tap(find.byTooltip('Start'));
    await _settle(tester);
    expect(pc.posts, ['/api/dev/docker/bbbbbbbbbbbb/start']);

    await tester.tap(find.byTooltip('Stop'));
    await _settle(tester);
    await tester.tap(find.text('Cancel'));
    await _settle(tester);
    expect(pc.posts.length, 1);

    await tester.tap(find.byTooltip('Stop'));
    await _settle(tester);
    await tester.tap(find.widgetWithText(FilledButton, 'Stop'));
    await _settle(tester);
    expect(pc.posts.last, '/api/dev/docker/aaaaaaaaaaaa/stop');

    await tester.pumpWidget(const SizedBox());
    c.dispose();
  });

  test('sessions alert only when a working one waits or finishes', () async {
    TestWidgetsFlutterBinding.ensureInitialized();
    var buzz = 0;
    final c = _controller(_Pc());
    final watch = KzSessionWatch(c, vibrate: () => buzz++);
    ClaudeSession s(String status) => ClaudeSession('1', 'Alpha', status);

    watch.apply([s('idle')]);
    watch.apply([s('busy')]);
    expect(watch.message.value, isNull);
    watch.apply([s('waiting')]);
    expect(watch.message.value, 'Alpha: Claude is waiting for you');
    watch.apply([s('busy')]);
    expect(watch.message.value, isNull);
    watch.apply([s('idle')]);
    expect(watch.message.value, 'Alpha: Claude finished');
    watch.apply([s('idle')]);
    expect(buzz, 2);

    watch.dispose();
    c.dispose();
  });
}
