import 'dart:convert';
import 'dart:typed_data';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/apps_page.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_running_apps.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

class _Pc implements HttpClientAdapter {
  _Pc({this.running = const []});

  List<Map<String, dynamic>> running;
  final posts = <String>[];
  int runningGets = 0;

  int actionStatus = 200;
  Map<String, dynamic> actionBody = {'ok': true, 'closed': true};

  @override
  void close({bool force = false}) {}

  @override
  Future<ResponseBody> fetch(RequestOptions o, Stream<Uint8List>? s, Future<void>? c) async {
    Object body = [];
    var status = 200;
    if (o.method == 'POST' && o.path.startsWith('/api/running-apps/')) {
      posts.add('${o.path}${o.queryParameters.isEmpty ? '' : '?${o.queryParameters.entries.map((e) => '${e.key}=${e.value}').join('&')}'}');
      status = actionStatus;
      body = actionBody;
    } else if (o.method == 'POST') {
      posts.add(o.path);
      body = {};
    } else if (o.path == '/api/running-apps') {
      runningGets++;
      body = {'apps': running};
    } else if (o.path.endsWith('/icon')) {
      return ResponseBody.fromBytes(const [], 404);
    } else if (o.path == '/api/apps') {
      body = [
        {'id': 'f1', 'name': 'Fav One', 'group': '', 'favorite': true},
        {'id': 'a1', 'name': 'Lib App', 'group': '', 'favorite': false},
      ];
    } else if (o.path == '/api/audio') {
      body = {
        'outputName': 'Speakers',
        'volume': 50,
        'muted': false,
        'inputMuted': false,
        'deafened': false,
        'sessions': [
          {'id': 's1', 'groupKey': 'aaaaaaaaaaaa', 'name': 'Discord', 'volume': 80, 'muted': false, 'active': true, 'system': false},
          {'id': 's2', 'groupKey': 'aaaaaaaaaaaa', 'name': 'Discord', 'volume': 40, 'muted': false, 'active': false, 'system': false},
          {'id': 's3', 'groupKey': 'cccccccccccc', 'name': 'Discord', 'volume': 10, 'muted': false, 'active': false, 'system': false},
        ],
      };
    }
    return ResponseBody.fromString(jsonEncode(body), status, headers: {
      Headers.contentTypeHeader: ['application/json'],
    });
  }
}

Map<String, dynamic> _app(String id, String name,
        {String title = '', bool fg = false, bool min = false, bool elevated = false, List<Map<String, dynamic>>? windows}) =>
    {
      'id': id,
      'name': name,
      'processName': name,
      'title': title,
      'foreground': fg,
      'minimized': min,
      'elevated': elevated,
      'windowCount': windows?.length ?? 1,
      'windows': windows ?? [{'id': 'w-$id.1', 'title': title, 'minimized': min, 'foreground': fg}],
    };

Future<(KzController, _Pc, SignalRService)> _setup(WidgetTester tester, _Pc pc) async {
  final signalR = SignalRService()..connectionStatus.value = 'Connected';
  final client = DroidDeckClient(baseUrl: 'http://127.0.0.1:1', dio: Dio()..httpClientAdapter = pc);
  final c = KzController(client: client, signalR: signalR);
  await tester.runAsync(() async {
    await c.refreshApps();
    await c.audio.refresh();
  });
  return (c, pc, signalR);
}

Future<void> _settle(WidgetTester tester) async {
  for (var i = 0; i < 6; i++) {
    await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 40)));
    await tester.pump(const Duration(milliseconds: 20));
  }
}

Future<void> _pump(WidgetTester tester, KzController c, {Size size = const Size(2340, 1080), double dpr = 2.75}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = dpr;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(MaterialApp(
    theme: buildAppTheme(const ThemeSettings(), Brightness.dark),
    home: Scaffold(
      body: Row(children: [const SizedBox(width: 73), Expanded(child: AppsPage(c: c))]),
    ),
  ));
  await tester.pump(const Duration(milliseconds: 200));
  await _settle(tester);
  await tester.pump(const Duration(milliseconds: 200));
}

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));
  tearDown(KzPrefs.resetDashboard);

  test('model: subtitle only when the title adds something; windows parsed', () {
    final a = KzRunningApp.fromJson(_app('x', 'Spotify', title: 'Spotify'));
    expect(a.subtitle, '');
    final b = KzRunningApp.fromJson(_app('y', 'Firefox', title: 'Docs - Firefox', windows: [
      {'id': 'a.1', 'title': 'Docs - Firefox', 'minimized': false, 'foreground': true},
      {'id': 'b.1', 'title': 'Mail', 'minimized': true, 'foreground': false},
    ]));
    expect(b.subtitle, 'Docs - Firefox');
    expect(b.windows.length, 2);
    expect(b.windows.last.minimized, isTrue);
  });

  testWidgets('shows running apps below Favorites, favorites still launch', (tester) async {
    final (c, pc, _) = await _setup(tester, _Pc(running: [_app('aaaaaaaaaaaa', 'Discord', title: 'General | Server', fg: true), _app('bbbbbbbbbbbb', 'Firefox')]));
    await _pump(tester, c);
    expect(tester.takeException(), isNull);

    expect(find.text('RUNNING NOW'), findsOneWidget);
    expect(find.text('Discord'), findsOneWidget);
    expect(find.text('General | Server'), findsOneWidget);
    expect(find.text('Firefox'), findsOneWidget);
    expect(tester.getTopLeft(find.text('RUNNING NOW')).dy > tester.getTopLeft(find.text('FAVORITES')).dy, isTrue);

    await tester.tap(find.text('Fav One'));
    await tester.pump();
    await _settle(tester);
    expect(pc.posts, contains('/api/apps/f1/launch'));
    c.dispose();
  });

  testWidgets('tapping Focus posts focus by id, never by title; no confirmation needed', (tester) async {
    final (c, pc, _) = await _setup(tester, _Pc(running: [_app('bbbbbbbbbbbb', 'Firefox', title: 'Docs')]));
    await _pump(tester, c);
    await tester.tap(find.text('Focus'));
    await tester.pump();
    await _settle(tester);
    await tester.pump();
    expect(pc.posts, ['/api/running-apps/bbbbbbbbbbbb/focus']);
    c.dispose();
  });

  testWidgets('minimized app offers Restore (a focus) and disables Minimize', (tester) async {
    final (c, pc, _) = await _setup(tester, _Pc(running: [_app('bbbbbbbbbbbb', 'Firefox', min: true)]));
    await _pump(tester, c);
    expect(find.text('Restore'), findsOneWidget);
    await tester.tap(find.byTooltip('Minimize'), warnIfMissed: false);
    await tester.pump();
    expect(pc.posts, isEmpty);
    await tester.tap(find.text('Restore'));
    await tester.pump();
    await _settle(tester);
    expect(pc.posts, ['/api/running-apps/bbbbbbbbbbbb/focus']);
    c.dispose();
  });

  testWidgets('Minimize posts minimize', (tester) async {
    final (c, pc, _) = await _setup(tester, _Pc(running: [_app('bbbbbbbbbbbb', 'Firefox')]));
    await _pump(tester, c);
    await tester.tap(find.byTooltip('Minimize'));
    await tester.pump();
    await _settle(tester);
    expect(pc.posts, ['/api/running-apps/bbbbbbbbbbbb/minimize']);
    c.dispose();
  });

  testWidgets('Close asks first; Cancel sends nothing; Confirm sends confirm=true', (tester) async {
    final (c, pc, _) = await _setup(tester, _Pc(running: [_app('bbbbbbbbbbbb', 'Firefox')]));
    await _pump(tester, c);

    await tester.tap(find.byTooltip('Close'));
    await tester.pumpAndSettle();
    expect(find.text('Close Firefox?'), findsOneWidget);
    expect(pc.posts, isEmpty, reason: 'nothing is sent before the user confirms');
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(pc.posts, isEmpty);

    await tester.tap(find.byTooltip('Close'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Close'));
    await tester.pump();
    await _settle(tester);
    await tester.pump();
    expect(pc.posts, ['/api/running-apps/bbbbbbbbbbbb/close?confirm=true']);
    c.dispose();
  });

  testWidgets('app that keeps its window after Close shows an honest message', (tester) async {
    final pc = _Pc(running: [_app('bbbbbbbbbbbb', 'Firefox')])..actionBody = {'ok': true, 'closed': false};
    final (c, _, _) = await _setup(tester, pc);
    await _pump(tester, c);
    await tester.tap(find.byTooltip('Close'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Close'));
    await tester.pump();
    await _settle(tester);
    await tester.pump();
    expect(find.textContaining('still open'), findsOneWidget);
    c.dispose();
  });

  testWidgets('app gone between list and action: friendly message and list refresh', (tester) async {
    final pc = _Pc(running: [_app('bbbbbbbbbbbb', 'Firefox')])
      ..actionStatus = 404
      ..actionBody = {'error': 'app_gone'};
    final (c, _, _) = await _setup(tester, pc);
    await _pump(tester, c);
    final before = pc.runningGets;
    await tester.tap(find.text('Focus'));
    await tester.pump();
    await _settle(tester);
    await tester.pump();
    expect(find.textContaining('already closed'), findsOneWidget);
    expect(pc.runningGets, greaterThan(before));
    c.dispose();
  });

  testWidgets('administrator app: controls disabled with an explanation, nothing is sent', (tester) async {
    final (c, pc, _) = await _setup(tester, _Pc(running: [_app('bbbbbbbbbbbb', 'Task Manager', elevated: true)]));
    await _pump(tester, c);
    expect(find.text('Runs as administrator'), findsOneWidget);
    await tester.tap(find.text('Focus'), warnIfMissed: false);
    await tester.tap(find.byTooltip('Close'), warnIfMissed: false);
    await tester.pump();
    expect(find.text('Close Task Manager?'), findsNothing);
    expect(pc.posts, isEmpty);
    c.dispose();
  });

  testWidgets('audio is associated by app identity (groupKey), not by name', (tester) async {
    final (c, pc, _) = await _setup(tester, _Pc(running: [
      _app('aaaaaaaaaaaa', 'Discord'),
      _app('dddddddddddd', 'Discord Canary'),
    ]));
    await _pump(tester, c);
    expect(find.byType(Slider), findsOneWidget);
    expect(find.text('80'), findsOneWidget);

    await tester.tap(find.byTooltip('Mute'));
    await tester.pump();
    await _settle(tester);
    final audioPosts = pc.posts.where((p) => p.contains('audio')).toList();
    expect(audioPosts, isEmpty);
    final grp = c.audio.state.value!.sessions.where((s) => s.groupKey == 'aaaaaaaaaaaa');
    expect(grp.every((s) => s.muted), isTrue);
    expect(c.audio.state.value!.sessions.firstWhere((s) => s.id == 's3').muted, isFalse);
    c.dispose();
  });

  testWidgets('several windows: expandable list, focus a specific window by its id', (tester) async {
    final (c, pc, _) = await _setup(tester, _Pc(running: [
      _app('bbbbbbbbbbbb', 'Firefox', title: 'Docs', windows: [
        {'id': 'a1.9', 'title': 'Docs', 'minimized': false, 'foreground': true},
        {'id': 'b2.9', 'title': 'Mail', 'minimized': false, 'foreground': false},
      ]),
    ]));
    await _pump(tester, c);
    expect(find.text('Mail'), findsNothing);
    await tester.tap(find.byTooltip('2 windows'));
    await tester.pump();
    await tester.ensureVisible(find.text('Mail'));
    await tester.pump();
    await tester.tap(find.text('Mail'));
    await tester.pump();
    await _settle(tester);
    expect(pc.posts, ['/api/running-apps/bbbbbbbbbbbb/focus?windowId=b2.9']);
    c.dispose();
  });

  testWidgets('push from the PC updates the list without polling', (tester) async {
    final (c, pc, signalR) = await _setup(tester, _Pc(running: [_app('bbbbbbbbbbbb', 'Firefox')]));
    await _pump(tester, c);
    final gets = pc.runningGets;
    signalR.runningAppsPush.value = [_app('bbbbbbbbbbbb', 'Firefox'), _app('eeeeeeeeeeee', 'Notepad')];
    await _settle(tester);
    expect(find.text('Notepad'), findsOneWidget);
    expect(pc.runningGets, gets, reason: 'the push carries the list; no extra request');
    signalR.runningAppsPush.value = [];
    await tester.pump();
    expect(find.text('No open applications.'), findsOneWidget);
    c.dispose();
  });

  testWidgets('setting off hides the section and stops watching', (tester) async {
    final (c, pc, _) = await _setup(tester, _Pc(running: [_app('bbbbbbbbbbbb', 'Firefox')]));
    await _pump(tester, c);
    expect(c.runningApps.active, isTrue);
    KzPrefs.showRunningApps.value = false;
    await tester.pump();
    await tester.pump();
    expect(find.text('RUNNING NOW'), findsNothing);
    expect(c.runningApps.active, isFalse);
    expect(find.text('Fav One'), findsOneWidget);
    c.dispose();
  });

  testWidgets('leaving the Apps page stops the PC from watching windows', (tester) async {
    final (c, pc, _) = await _setup(tester, _Pc(running: [_app('bbbbbbbbbbbb', 'Firefox')]));
    await _pump(tester, c);
    expect(c.runningApps.active, isTrue);
    await tester.pumpWidget(const SizedBox());
    expect(c.runningApps.active, isFalse);
    c.dispose();
  });

  for (final (name, size, dpr) in [
    ('Mate 20 Lite landscape', const Size(2340, 1080), 2.75),
    ('small landscape', const Size(1920, 1080), 3.0),
    ('portrait phone', const Size(1080, 2340), 2.75),
  ]) {
    testWidgets('layout: many apps, windows and audio, no overflow on $name', (tester) async {
      final apps = [
        for (var i = 0; i < 14; i++)
          _app(i == 0 ? 'aaaaaaaaaaaa' : 'x${i.toString().padLeft(11, '0')}', i == 0 ? 'Discord' : 'Some Long Application Name Number $i',
              title: 'A really long window title that must be ellipsized properly $i', fg: i == 1, min: i == 2, elevated: i == 3),
      ];
      final (c, _, _) = await _setup(tester, _Pc(running: apps));
      await _pump(tester, c, size: size, dpr: dpr);
      expect(tester.takeException(), isNull, reason: 'overflow on $name');
      c.dispose();
    });
  }
}
