import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/dashboard_page.dart';
import 'package:droiddeck/src/kz/kz_audio.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';

/// Sem rede: responde na hora com dados de exemplo (sem timers de timeout do Dio pendentes).
class _FakePc implements HttpClientAdapter {
  _FakePc(this.sessions);
  final int sessions;

  @override
  void close({bool force = false}) {}

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream, Future<void>? cancelFuture) async {
    String body = '[]';
    if (options.path == '/api/audio') {
      final list = [
        for (var i = 0; i < sessions; i++)
          '{"id":"s$i","name":"${i == 0 ? 'Call of Duty' : 'Some Application $i'}","volume":${100 - i * 4},"muted":${i == 3},"active":${i < 3},"system":false}'
      ].join(',');
      body = '{"outputName":"Speakers","volume":62,"muted":false,"inputMuted":false,"deafened":false,"sessions":[$list]}';
    } else if (options.path == '/api/apps') {
      body = '[${[for (var i = 0; i < 9; i++) '{"id":"$i","name":"Application $i","group":"","favorite":true}'].join(',')}]';
    }
    return ResponseBody.fromString(body, 200, headers: {
      Headers.contentTypeHeader: ['application/json'],
    });
  }
}

Future<void> _pumpAt(WidgetTester tester, Size physical, double dpr, {required String name, int sessions = 8}) async {
  tester.view.physicalSize = physical;
  tester.view.devicePixelRatio = dpr;
  addTearDown(tester.view.reset);

  final signalR = SignalRService();
  signalR.connectionStatus.value = 'Connected';
  signalR.systemStats.value = SystemStats(
    cpuUsage: 42,
    gpuUsage: 67,
    gpuTemp: 65,
    ramUsage: 48,
    ramTotal: 32768,
    ramAvailable: 17000,
  );
  final dio = Dio()..httpClientAdapter = _FakePc(sessions);
  final client = DroidDeckClient(baseUrl: 'http://127.0.0.1:1', dio: dio);
  final c = KzController(client: client, signalR: signalR);
  await tester.runAsync(() async {
    await c.refreshApps();
    await c.audio.refresh();
  });

  final key = GlobalKey();
  await tester.pumpWidget(
    RepaintBoundary(
      key: key,
      child: MaterialApp(
        debugShowCheckedModeBanner: false,
        theme: buildAppTheme(const ThemeSettings(), Brightness.dark),
        home: Scaffold(
          body: SafeArea(
            child: Row(children: [
              const SizedBox(width: 73), // faixa de navegacao
              Expanded(
                child: Column(children: [
                  const SizedBox(height: 30), // cabecalho compacto
                  Expanded(child: DashboardPage(c: c, onOpenApps: () {})),
                ]),
              ),
            ]),
          ),
        ),
      ),
    ),
  );
  await tester.pump(const Duration(milliseconds: 300));
  final problem = tester.takeException();
  if (problem != null) c.dispose();
  expect(problem, isNull, reason: 'overflow/erro de layout em $name');

  await tester.runAsync(() async {
    final boundary = key.currentContext!.findRenderObject() as RenderRepaintBoundary;
    final image = await boundary.toImage(pixelRatio: 1);
    final bytes = (await image.toByteData(format: ui.ImageByteFormat.png))!;
    final dir = Directory('build/layout')..createSync(recursive: true);
    File('${dir.path}/$name.png').writeAsBytesSync(Uint8List.view(bytes.buffer));
  });
  c.dispose();
}

void main() {
  testWidgets('Huawei Mate 20 Lite landscape (2340x1080 @2.75)', (tester) async {
    await _pumpAt(tester, const Size(2340, 1080), 2.75, name: 'mate20lite');
  });
  testWidgets('smaller landscape (1920x1080 @3)', (tester) async {
    await _pumpAt(tester, const Size(1920, 1080), 3, name: 'small_landscape');
  });
  testWidgets('many audio sessions scroll without overflow', (tester) async {
    await _pumpAt(tester, const Size(2340, 1080), 2.75, name: 'many_sessions', sessions: 20);
  });
}
