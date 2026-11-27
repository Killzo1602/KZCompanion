import 'dart:convert';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/ai_usage_page.dart';
import 'package:droiddeck/src/kz/hardware_page.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_shell.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

class _Pc implements HttpClientAdapter {
  _Pc({this.aiStatus = 200});
  final int aiStatus;
  int aiGets = 0;

  @override
  void close({bool force = false}) {}

  @override
  Future<ResponseBody> fetch(RequestOptions o, Stream<Uint8List>? s, Future<void>? c) async {
    Object body = [];
    var status = 200;
    if (o.path == '/api/ai-usage') {
      aiGets++;
      status = aiStatus;
      body = {
        'providers': [
          {
            'id': 'claude',
            'name': 'Claude Code',
            'installed': true,
            'limits': [
              {'label': '5-hour', 'percent': 42, 'resetsAt': null},
              {'label': 'Weekly', 'percent': 7.4, 'resetsAt': null},
            ],
            'tokensToday': 1250000,
            'tokensWeek': 9100,
            'error': null,
          },
          {'id': 'codex', 'name': 'Codex', 'installed': false, 'limits': []},
        ],
      };
    } else if (o.path == '/api/apps') {
      body = [
        {'id': 'f', 'name': 'Fav One', 'group': '', 'favorite': true}
      ];
    } else if (o.path == '/api/audio') {
      body = {'outputName': 'Speakers', 'volume': 50, 'muted': false, 'inputMuted': false, 'deafened': false, 'sessions': []};
    }
    return ResponseBody.fromString(jsonEncode(body), status, headers: {
      Headers.contentTypeHeader: ['application/json'],
    });
  }
}

KzController _controller(_Pc pc) {
  final signalR = SignalRService()..connectionStatus.value = 'Connected';
  final client = DroidDeckClient(baseUrl: 'http://127.0.0.1:1', dio: Dio()..httpClientAdapter = pc);
  return KzController(client: client, signalR: signalR);
}

Future<void> _settle(WidgetTester tester) async {
  for (var i = 0; i < 8; i++) {
    await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 5)));
    await tester.pump(const Duration(milliseconds: 100));
  }
}

int _selected(WidgetTester tester) => List.generate(7, (i) => i).firstWhere((i) =>
    tester
        .getSize(find.descendant(of: find.byKey(ValueKey('dot-$i')), matching: find.byType(AnimatedContainer)))
        .width >
    10);

Future<void> _pumpShell(WidgetTester tester, _Pc pc, {Size size = const Size(850, 393)}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(MaterialApp(
    theme: buildAppTheme(const ThemeSettings(), Brightness.dark),
    home: KzShell(controller: _controller(pc)),
  ));
  await _settle(tester);
}

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues({});
    TestWidgetsFlutterBinding.ensureInitialized().defaultBinaryMessenger.setMockMessageHandler(
        'dev.flutter.pigeon.wakelock_plus_platform_interface.WakelockPlusApi.toggle',
        (_) async => const StandardMessageCodec().encodeMessage(<Object?>[null]));
  });

  testWidgets('swiping changes the section and saves it by name', (tester) async {
    await _pumpShell(tester, _Pc());
    expect(_selected(tester), 0);

    await tester.flingFrom(const Offset(450, 330), const Offset(-350, 0), 1500);
    await _settle(tester);
    expect(_selected(tester), 1);
    expect((await SharedPreferences.getInstance()).getString('kz_page'), 'Apps');

    await tester.flingFrom(const Offset(250, 330), const Offset(350, 0), 1500);
    await _settle(tester);
    expect(_selected(tester), 0);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('dragging a volume slider does not change the section', (tester) async {
    await _pumpShell(tester, _Pc());
    await tester.drag(find.byType(Slider).first, const Offset(-200, 0));
    await _settle(tester);
    expect(_selected(tester), 0);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('a far jump from the rail lands there, and the saved section is restored', (tester) async {
    final pc = _Pc();
    await _pumpShell(tester, pc);
    await tester.tap(find.byKey(const ValueKey('dot-4')));
    await _settle(tester);
    expect(_selected(tester), 4);
    expect(find.text('CLAUDE CODE'), findsOneWidget);

    await tester.pumpWidget(const SizedBox());
    await _pumpShell(tester, pc);
    expect(_selected(tester), 4);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('the section name opens a list to jump', (tester) async {
    await _pumpShell(tester, _Pc());
    await tester.tap(find.text('Dashboard'));
    await _settle(tester);
    await tester.tap(find.text('Hardware'));
    await _settle(tester);
    expect(_selected(tester), 2);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('the optional side bar fits a short phone by scrolling', (tester) async {
    SharedPreferences.setMockInitialValues({'kz_side_bar': true});
    await KzPrefs.load();
    addTearDown(() => KzPrefs.sideBar.value = false);
    await _pumpShell(tester, _Pc(), size: const Size(640, 320));
    expect(find.byType(NavigationRail), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('a whole row of favorites is visible without scrolling', (tester) async {
    await _pumpShell(tester, _Pc());
    final tile = tester.getRect(find.byType(KzAppTile));
    final card = tester.getRect(find.ancestor(of: find.byType(KzAppTile), matching: find.byType(KzCard)).first);
    expect(tile.bottom, lessThanOrEqualTo(card.bottom - 4));
    await tester.pumpWidget(const SizedBox());
  });

  for (final size in const [Size(850, 393), Size(640, 320)]) {
    testWidgets('Hardware fits one screen without scrolling at $size', (tester) async {
      tester.view.physicalSize = size;
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final c = _controller(_Pc());
      c.signalR.systemStats.value = SystemStats(
        gpuTemp: 60,
        gpuHotspot: 70,
        gpuMemUsed: 4000,
        gpuMemTotal: 16000,
        ramTotal: 32000,
        fans: [for (var i = 0; i < 8; i++) FanStat('Fan $i', 900)],
        disks: [for (final n in ['C:', 'D:', 'E:', 'F:']) DiskStat(name: n, totalBytes: 100, freeBytes: 40)],
      );
      await tester.pumpWidget(MaterialApp(
        theme: buildAppTheme(const ThemeSettings(), Brightness.dark),
        home: Scaffold(body: HardwarePage(c: c)),
      ));
      await _settle(tester);
      expect(tester.takeException(), isNull);
      expect(find.byType(Scrollable), findsNothing);
      for (final title in ['CPU', 'GPU', 'MEMORY', 'STORAGE', 'FANS', 'NETWORK']) {
        expect(find.text(title), findsOneWidget);
      }
      await tester.pumpWidget(const SizedBox());
      c.dispose();
    });
  }

  testWidgets('AI page shows limits, tokens and a missing tool', (tester) async {
    final c = _controller(_Pc());
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: AiUsagePage(c: c))));
    await _settle(tester);
    expect(find.textContaining('42%', findRichText: true), findsOneWidget);
    expect(find.textContaining('1.3M today'), findsOneWidget);
    expect(find.text('Not found on this PC.'), findsOneWidget);
    final cards = find.byType(AiProviderCard);
    expect(tester.getSize(cards.at(0)).height, tester.getSize(cards.at(1)).height);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    c.dispose();
  });

  testWidgets('AI page on an older PC asks to update it', (tester) async {
    final c = _controller(_Pc(aiStatus: 404));
    await tester.pumpWidget(MaterialApp(home: Scaffold(body: AiUsagePage(c: c))));
    await _settle(tester);
    expect(find.textContaining('Update KZ Companion'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    c.dispose();
  });

  test('pace: plenty, on pace, runs out early, and silence when it cannot tell', () {
    final now = DateTime.utc(2026, 1, 1);
    const window = Duration(hours: 5);
    final half = now.add(const Duration(hours: 2, minutes: 30));
    expect(kzPace(20, half, window, now)!.verdict, 'Plenty left');
    expect(kzPace(45, half, window, now)!.verdict, 'On pace');
    expect(kzPace(80, half, window, now)!.verdict, 'Full in 37m');
    expect(kzPace(100, half, window, now)!.verdict, 'Limit reached');
    expect(kzPace(3, now.add(const Duration(hours: 4, minutes: 55)), window, now)!.verdict, isNull);
    expect(kzPace(50, null, window, now), isNull);
    expect(kzPace(50, now.subtract(const Duration(minutes: 1)), window, now), isNull);
  });

  test('reset countdown text', () {
    final now = DateTime.utc(2026, 1, 1);
    expect(fmtResetIn(null, now), '');
    expect(fmtResetIn(now.add(const Duration(minutes: 12)), now), 'Resets in 12m');
    expect(fmtResetIn(now.add(const Duration(hours: 2, minutes: 5)), now), 'Resets in 2h 5m');
    expect(fmtResetIn(now.add(const Duration(days: 3, hours: 4)), now), 'Resets in 3d 4h');
  });
}
