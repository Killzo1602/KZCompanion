import 'dart:convert';
import 'dart:typed_data';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/ai_usage_page.dart';
import 'package:droiddeck/src/kz/dashboard_page.dart';
import 'package:droiddeck/src/kz/grid/kz_grid_layout.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

class _Pc implements HttpClientAdapter {
  @override
  void close({bool force = false}) {}

  @override
  Future<ResponseBody> fetch(RequestOptions o, Stream<Uint8List>? s, Future<void>? c) async {
    Object body = [];
    if (o.path == '/api/ai-usage') {
      final resets = DateTime.now().add(const Duration(hours: 2)).toIso8601String();
      body = {
        'providers': [
          {
            'id': 'claude', 'name': 'Claude Code', 'installed': true, 'tokensToday': 5, 'tokensWeek': 9,
            'limits': [
              for (final label in ['5-hour', 'Weekly'])
                {'label': label, 'percent': 80, 'resetsAt': resets, 'windowSeconds': 18000},
            ],
          },
        ],
      };
    } else if (o.path == '/api/audio') {
      body = {
        'outputName': 'A very long speaker name (High Definition Audio Device)',
        'volume': 50, 'muted': false, 'inputMuted': false, 'deafened': false,
        'sessions': [{'id': 's', 'name': 'Some Application', 'volume': 40}],
      };
    } else if (o.path == '/api/apps') {
      body = [{'id': 'f', 'name': 'Favorite application', 'group': '', 'favorite': true}];
    }
    return ResponseBody.fromString(jsonEncode(body), 200, headers: {
      Headers.contentTypeHeader: ['application/json'],
    });
  }
}

Future<void> _settle(WidgetTester tester) async {
  for (var i = 0; i < 6; i++) {
    await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 5)));
    await tester.pump(const Duration(milliseconds: 100));
  }
}

Future<KzController> _pump(WidgetTester tester, {Size size = const Size(567, 330)}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final signalR = SignalRService()..connectionStatus.value = 'Connected';
  final client = DroidDeckClient(baseUrl: 'http://127.0.0.1:1', dio: Dio()..httpClientAdapter = _Pc());
  final c = KzController(client: client, signalR: signalR);
  await tester.pumpWidget(MaterialApp(
    theme: buildAppTheme(const ThemeSettings(), Brightness.dark),
    home: Scaffold(body: DashboardPage(c: c, onOpenApps: () {})),
  ));
  await _settle(tester);
  return c;
}

Future<void> _end(WidgetTester tester, KzController c) async {
  await tester.pumpWidget(const SizedBox());
  c.dispose();
}

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues({});
    KzGrid.layout.value = null;
    KzGrid.editing.value = false;
  });

  test('widgets never overlap or leave the page', () {
    expect(gridStart.every((i) => gridFits(gridStart, i)), isTrue);
    expect(gridFits(gridStart, const GridItem(GridType.cpu, 1, 0, 2, 3)), isFalse);
    expect(gridFits(gridStart, const GridItem(GridType.clock, 11, 0, 2, 1)), isFalse);
    expect(gridFreeSpot(gridStart, GridType.clock), isNull);
    expect(gridFreeSpot(const [GridItem(GridType.cpu, 0, 0, 2, 3)], GridType.clock),
        const GridItem(GridType.clock, 2, 0, 3, 2));
  });

  test('saved layout: round trip, and bad entries are dropped', () {
    expect(gridDecode(gridEncode(gridStart)), gridStart);
    expect(gridDecode(null), isNull);
    expect(gridDecode('not json'), isNull);
    final items = gridDecode('[{"type":"nope","x":0,"y":0,"w":2,"h":2},'
        '{"type":"cpu","x":0,"y":0,"w":2,"h":2},{"type":"gpu","x":1,"y":0,"w":2,"h":2}]');
    expect(items, const [GridItem(GridType.cpu, 0, 0, 2, 2)]);
  });

  testWidgets('opening the editor and pressing Done keeps the default dashboard', (tester) async {
    final c = await _pump(tester);
    KzGrid.editing.value = true;
    await _settle(tester);
    await tester.tap(find.text('Done'));
    await _settle(tester);
    expect(KzGrid.layout.value, isNull);
    expect((await SharedPreferences.getInstance()).getString(KzGrid.key), isNull);
    await _end(tester, c);
  });

  testWidgets('remove, move and resize are saved; Default restores', (tester) async {
    final c = await _pump(tester, size: const Size(777, 363));
    KzGrid.editing.value = true;
    await _settle(tester);

    await tester.tap(find.byKey(const ValueKey('remove-favorites')));
    await _settle(tester);
    await tester.drag(find.byKey(const ValueKey('move-cpu')), const Offset(0, 190), warnIfMissed: false);
    await _settle(tester);
    await tester.drag(find.byKey(const ValueKey('resize-cpu')), const Offset(140, 0));
    await _settle(tester);
    await tester.tap(find.text('Done'));
    await _settle(tester);

    final saved = gridDecode((await SharedPreferences.getInstance()).getString(KzGrid.key))!;
    expect(saved.any((i) => i.type == GridType.favorites), isFalse);
    final cpu = saved.firstWhere((i) => i.type == GridType.cpu);
    expect(cpu.y, 3);
    expect(cpu.w, greaterThan(2));
    expect(find.text('FAVORITES'), findsNothing);

    KzGrid.editing.value = true;
    await _settle(tester);
    await tester.tap(find.text('Default'));
    await _settle(tester);
    expect(KzGrid.layout.value, isNull);
    expect(find.text('FAVORITES'), findsOneWidget);
    await _end(tester, c);
  });

  testWidgets('the AI tile shows the whole card without scrolling', (tester) async {
    KzGrid.layout.value = const [GridItem(GridType.claude, 0, 0, 4, 3)];
    final c = await _pump(tester, size: const Size(777, 363));
    final tile = tester.getSize(find.byType(SingleChildScrollView));
    expect(tester.getSize(find.byType(AiProviderCard)).height, lessThanOrEqualTo(tile.height));
    await _end(tester, c);
  });

  for (final type in GridType.values) {
    testWidgets('${type.name} at minimum size does not overflow', (tester) async {
      KzGrid.layout.value = [GridItem(type, 0, 0, type.minW, type.minH)];
      final c = await _pump(tester);
      expect(tester.takeException(), isNull);
      await _end(tester, c);
    });
  }
}
