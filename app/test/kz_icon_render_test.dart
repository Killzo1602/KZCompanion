// Manual: renders real extracted icons (KZ_ICON_JSON = GET /api/apps) at the Huawei's density.
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final path = Platform.environment['KZ_ICON_JSON'];
  testWidgets('real icons at 48dp tiles and 26dp mixer size, 2.75x', (tester) async {
    tester.view.physicalSize = const Size(1500, 800);
    tester.view.devicePixelRatio = 2.75;
    addTearDown(tester.view.reset);
    final apps = [for (final j in (jsonDecode(File(path!).readAsStringSync()) as List)) KzApp.fromJson((j as Map).cast<String, dynamic>())];
    final key = GlobalKey();
    await tester.pumpWidget(RepaintBoundary(
      key: key,
      child: MaterialApp(
        theme: buildAppTheme(const ThemeSettings(), Brightness.dark),
        home: Scaffold(
          body: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Wrap(spacing: 10, runSpacing: 10, children: [for (final a in apps) KzAppTile(app: a, onTap: () {})]),
              const SizedBox(height: 14),
              Row(children: [for (final a in apps) Padding(padding: const EdgeInsets.only(right: 10), child: KzAppIcon(a, size: 26))]),
            ]),
          ),
        ),
      ),
    ));
    for (var i = 0; i < 5; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 150))); // decodifica as imagens
      await tester.pump();
    }
    expect(tester.takeException(), isNull);
    await tester.runAsync(() async {
      final b = key.currentContext!.findRenderObject() as RenderRepaintBoundary;
      final img = await b.toImage(pixelRatio: 1);
      final bytes = (await img.toByteData(format: ui.ImageByteFormat.png))!;
      File('build/layout/real_icons.png')
        ..createSync(recursive: true)
        ..writeAsBytesSync(Uint8List.view(bytes.buffer));
    });
  }, skip: path == null);
}
