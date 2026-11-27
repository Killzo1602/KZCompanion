import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_appearance_page.dart';
import 'package:droiddeck/src/kz/kz_integrations_page.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));
  tearDown(KzPrefs.resetDashboard);

  test('saved DroidDeck settings still load: unknown themes fall back to KZ Graphite, accent and mode kept', () async {
    SharedPreferences.setMockInitialValues({
      'theme_preset': 'synthwave',
      'theme_mode': 'light',
      'theme_accent': 0xFFFDD400,
      'theme_tile_style': 'frame',
      'deck_only': true,
      'app_language': 'pt',
    });
    await ThemeController.instance.load();
    final s = ThemeController.instance.settings.value;
    expect(s.preset.id, 'kz');
    expect(s.mode, ThemeMode.light);
    expect(s.accent!.toARGB32(), 0xFFFDD400);
    expect(AppLanguage.current.value, 'en');
  });

  test('dashboard prefs persist and restore', () async {
    SharedPreferences.setMockInitialValues({});
    await KzPrefs.load();
    expect(KzPrefs.showStats.value && KzPrefs.showFavorites.value && KzPrefs.showAudio.value, isTrue);
    await KzPrefs.setShowFavorites(false);
    await KzPrefs.setDensity(KzDensity.compact);
    await KzPrefs.setBackground(KzBackground.glow);
    KzPrefs.showFavorites.value = true;
    await KzPrefs.load();
    expect(KzPrefs.showFavorites.value, isFalse);
    expect(KzPrefs.density.value, KzDensity.compact);
    expect(KzPrefs.background.value, KzBackground.glow);
    await KzPrefs.resetDashboard();
    expect(KzPrefs.density.value, KzDensity.comfortable);
    expect(KzPrefs.showFavorites.value, isTrue);
  });

  testWidgets('Appearance page fits the Mate 20 Lite in landscape and its controls work', (tester) async {
    SharedPreferences.setMockInitialValues({});
    tester.view.physicalSize = const Size(2340, 1080);
    tester.view.devicePixelRatio = 2.75;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(MaterialApp(
      theme: buildAppTheme(const ThemeSettings(), Brightness.dark),
      home: const KzAppearancePage(),
    ));
    await tester.pump();
    expect(tester.takeException(), isNull);
    expect(find.text('THEME'), findsOneWidget);
    expect(find.text('DASHBOARD'), findsOneWidget);
    for (final legacy in ['Buttons', 'Button style', 'Haptics', 'Language', 'Deck only']) {
      expect(find.textContaining(legacy), findsNothing);
    }
    await tester.ensureVisible(find.text("Favorites"));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Favorites'));
    await tester.pump();
    expect(KzPrefs.showFavorites.value, isFalse);
    await tester.ensureVisible(find.text('Black'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Black'));
    await tester.pump();
    expect(ThemeController.instance.settings.value.preset.id, 'oled');
    await ThemeController.instance.update(const ThemeSettings());
  });

  testWidgets('Integrations page lists the catalog, with no old DroidDeck screens', (tester) async {
    await tester.pumpWidget(MaterialApp(
      theme: buildAppTheme(const ThemeSettings(), Brightness.dark),
      home: KzIntegrationsPage(signalR: SignalRService(), canConfigure: false),
    ));
    expect(find.text('Discord'), findsOneWidget);
    expect(find.text('Spotify'), findsOneWidget);
    for (final legacy in ['Buttons', 'Profiles', 'Automations', 'Stream Deck']) {
      expect(find.textContaining(legacy), findsNothing);
    }
  });
}
