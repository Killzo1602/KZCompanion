import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/config/kz_config_shell.dart';
import 'package:droiddeck/src/kz/kz_integration_catalog.dart';
import 'package:droiddeck/src/kz/kz_integrations_page.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

const _legacy = ['Buttons', 'Profiles', 'Automations', 'Import from Stream Deck', 'Stream Deck'];

class _Pc implements HttpClientAdapter {
  _Pc({this.spotifyConnected = false, this.mediaIds = const []});
  final bool spotifyConnected;
  final List<String> mediaIds;

  @override
  void close({bool force = false}) {}

  @override
  Future<ResponseBody> fetch(RequestOptions o, Stream<Uint8List>? s, Future<void>? c) async {
    Object body = {};
    var status = 200;
    switch (o.path) {
      case '/api/integrations':
        body = [
          {'name': 'spotify', 'title': 'Spotify', 'connected': spotifyConnected, 'account': spotifyConnected ? 'kz' : null},
          {'name': 'obs', 'title': 'OBS Studio', 'connected': false, 'account': null},
          {'name': 'homeassistant', 'title': 'Home Assistant', 'connected': false, 'account': null},
        ];
      case '/api/integrations/spotify':
        body = {'connected': spotifyConnected, 'configured': spotifyConnected, 'account': spotifyConnected ? 'kz' : null, 'fields': {'clientId': ''}};
      case '/api/discord/state':
        body = {'configured': false, 'connected': false};
      case '/api/obs/state':
        body = {'connected': false};
      case '/api/v1/Media/sessions':
        body = [for (final id in mediaIds) {'id': id, 'title': 't', 'playbackStatus': 'Playing'}];
      default:
        status = 404;
    }
    return ResponseBody.fromString(jsonEncode(body), status, headers: {
      Headers.contentTypeHeader: ['application/json'],
    });
  }
}

Future<void> _pump(WidgetTester tester, Widget home, {Size size = const Size(2340, 1080), double dpr = 2.75}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = dpr;
  addTearDown(tester.view.reset);
  await tester.pumpWidget(MaterialApp(theme: buildAppTheme(const ThemeSettings(), Brightness.dark), home: home));
  for (var i = 0; i < 4; i++) {
    await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 30)));
    await tester.pump(const Duration(milliseconds: 20));
  }
}

Widget _integrations({required bool canConfigure, _Pc? pc}) {
  final client = DroidDeckClient(baseUrl: 'http://127.0.0.1:1', dio: Dio()..httpClientAdapter = pc ?? _Pc());
  return KzIntegrationsPage(client: client, signalR: SignalRService(), canConfigure: canConfigure);
}

void main() {
  group('Windows configurator navigation', () {
    testWidgets('has exactly Applications, Integrations and Appearance', (tester) async {
      await _pump(tester, const KzConfigShell(pages: [Text('p-apps'), Text('p-integrations'), Text('p-appearance')]));
      final rail = tester.widget<NavigationRail>(find.byType(NavigationRail));
      final labels = rail.destinations.map((d) => (d.label as Text).data).toList();
      expect(labels, ['Applications', 'Integrations', 'Appearance']);
      expect(labels, KzConfigShell.destinationLabels);
      for (final word in _legacy) {
        expect(find.textContaining(word), findsNothing, reason: 'legacy "$word" is back in the configurator');
      }
    });

    testWidgets('Integrations is its own page, not inside another menu', (tester) async {
      await _pump(tester, const KzConfigShell(pages: [Text('p-apps'), Text('p-integrations'), Text('p-appearance')]));
      await tester.tap(find.text('Integrations'));
      await tester.pump();
      expect(find.text('p-integrations'), findsOneWidget);
      expect(find.byType(ExpansionTile), findsNothing);
      expect(find.byType(PopupMenuButton), findsNothing);
    });

    test('source: no legacy deck wording in app/lib', () {
      final hits = <String>[];
      for (final f in Directory('lib').listSync(recursive: true).whereType<File>().where((f) => f.path.endsWith('.dart'))) {
        final text = f.readAsStringSync();
        for (final word in _legacy) {
          if (RegExp('''['"][^'"\\n]*\\b$word\\b[^'"\\n]*['"]''').hasMatch(text)) hits.add('${f.path}: $word');
        }
      }
      expect(hits, isEmpty, reason: 'removed deck screens must not return');
    });

    test('source: the removed deck screens stay deleted', () {
      for (final gone in [
        'lib/src/pages',
        'lib/src/stream_deck',
        'lib/src/profiles',
        'lib/src/kz/config/profiles_page.dart',
        'lib/src/kz/config/buttons_page.dart',
      ]) {
        expect(FileSystemEntity.typeSync(gone), anyOf(FileSystemEntityType.notFound, FileSystemEntityType.directory),
            reason: gone);
      }
      expect(File('lib/src/stream_deck').existsSync(), isFalse);
      expect(Directory('lib/src/stream_deck').existsSync(), isFalse);
      expect(Directory('lib/src/profiles').existsSync(), isFalse);
      expect(File('lib/src/kz/config/buttons_page.dart').existsSync(), isFalse);
      expect(File('lib/src/kz/config/profiles_page.dart').existsSync(), isFalse);
    });

    test('web page metadata does not advertise a button deck', () {
      for (final f in ['web/index.html', 'web/manifest.json']) {
        expect(File(f).readAsStringSync().toLowerCase(), isNot(contains('deck de bot')), reason: f);
      }
    });
  });

  group('Integrations catalog', () {
    test('priority order is Discord, Spotify, OBS, Apple Music', () {
      expect(KzCatalog.priority.map((i) => i.id), ['discord', 'spotify', 'obs', 'apple_music']);
    });

    test('Apple Music is a candidate, never a working native API', () {
      final a = KzCatalog.priority.last;
      expect(a.support, KzSupport.candidate);
      expect(a.capabilities.where((c) => c.contains('unverified')).length, greaterThanOrEqualTo(3));
      expect(a.summary.toLowerCase(), contains('not a native'));
      expect(a.verification.toLowerCase(), contains('nothing here has been tested'));
      expect(a.backendName, isNull);
    });

    test('only integrations with a setup screen say "Setup available"', () {
      expect(KzCatalog.all.where((i) => i.support == KzSupport.setupAvailable).map((i) => i.id), ['discord', 'spotify', 'obs']);
      expect(KzCatalog.all.firstWhere((i) => i.id == 'tuya').support, KzSupport.onHold);
    });

    test('Discord requirements do not claim that no approval is needed', () {
      final text = KzCatalog.priority.first.requirements.join(' ').toLowerCase();
      expect(text, contains('approved'));
      expect(text, contains('tester'));
      expect(text, isNot(contains('no approval')));
    });

    testWidgets('PC view: catalog with status from the PC, Configure available', (tester) async {
      await _pump(tester, _integrations(canConfigure: true, pc: _Pc(spotifyConnected: true, mediaIds: ['AppleInc.AppleMusicWin_x!App'])), size: const Size(1000, 4000), dpr: 1);
      for (final n in ['Discord', 'Spotify', 'OBS Studio', 'Apple Music', 'Home Assistant', 'Tuya / Smart Life']) {
        expect(find.text(n), findsOneWidget, reason: n);
      }
      expect(find.textContaining('Connected · kz'), findsOneWidget);
      expect(find.text('Not set up'), findsNWidgets(2));
      expect(find.text('Not connected'), findsNothing);
      expect(find.text('Candidate'), findsOneWidget);
      expect(find.text('On hold'), findsOneWidget);
      expect(find.text('Backend only'), findsWidgets);
      expect(find.textContaining('Passwords, secrets and logins are entered on your PC'), findsNothing);
    });

    testWidgets('PC view: detail page offers setup for Discord, Spotify and OBS', (tester) async {
      await _pump(tester, _integrations(canConfigure: true), size: const Size(1000, 4000), dpr: 1);
      await tester.tap(find.byKey(const ValueKey('integration-obs')));
      await _settle(tester);
      expect(find.text('Save and connect'), findsOneWidget);
      expect(find.widgetWithText(TextField, 'Password'), findsOneWidget);
      Navigator.of(tester.element(find.text('Save and connect'))).pop();
      await _settle(tester);
      await tester.tap(find.byKey(const ValueKey('integration-spotify')));
      await _settle(tester);
      expect(find.text('Log in with Spotify'), findsOneWidget);
    });

    testWidgets('Apple Music detail: honest about being unverified, no setup button', (tester) async {
      await _pump(tester, _integrations(canConfigure: true, pc: _Pc(mediaIds: ['AppleInc.AppleMusicWin_x!App'])), size: const Size(1000, 4000), dpr: 1);
      await tester.ensureVisible(find.byKey(const ValueKey('integration-apple_music')));
      await tester.tap(find.byKey(const ValueKey('integration-apple_music')));
      await _settle(tester);
      expect(find.textContaining('Playback controls have not been tested'), findsOneWidget);
      expect(find.text('There is nothing to set up yet.'), findsOneWidget);
      expect(find.byType(FilledButton), findsNothing);
      expect(find.textContaining('MusicKit'), findsOneWidget);
    });

    testWidgets('Android view: status only, secrets are never asked for', (tester) async {
      await _pump(tester, _integrations(canConfigure: false), size: const Size(1000, 4000), dpr: 1);
      expect(find.textContaining('entered on your PC, never on this device'), findsOneWidget);
      for (final key in ['discord', 'spotify', 'obs']) {
        await tester.ensureVisible(find.byKey(ValueKey('integration-$key')));
        await tester.tap(find.byKey(ValueKey('integration-$key')));
        await _settle(tester);
        expect(find.byType(TextField), findsNothing, reason: '$key must not ask for credentials on Android');
        expect(find.textContaining('Credentials are never entered on the phone'), findsOneWidget);
        expect(find.widgetWithText(FilledButton, 'Set up Discord'), findsNothing);
        Navigator.of(tester.element(find.textContaining('Credentials are never entered'))).pop();
        await _settle(tester);
      }
    });

    testWidgets('layout fits the Huawei Mate 20 Lite landscape', (tester) async {
      await _pump(tester, _integrations(canConfigure: false), size: const Size(2340, 1080), dpr: 2.75);
      expect(tester.takeException(), isNull);
      await tester.drag(find.byType(ListView).first, const Offset(0, -2000));
      await tester.pump();
      expect(tester.takeException(), isNull);
    });
  });
}

Future<void> _settle(WidgetTester tester) async {
  for (var i = 0; i < 5; i++) {
    await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 30)));
    await tester.pump(const Duration(milliseconds: 250));
  }
}
