import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:signals_flutter/signals_flutter.dart';

import 'app_theme.dart';

class ThemeController {
  ThemeController._();
  static final instance = ThemeController._();

  static const _presetKey = 'theme_preset';
  static const _modeKey = 'theme_mode';
  static const _accentKey = 'theme_accent';

  final settings = signal(const ThemeSettings());

  Future<void> load() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final accent = prefs.getInt(_accentKey);
      settings.value = ThemeSettings(
        presetId: presetById(prefs.getString(_presetKey)).id,
        mode: ThemeMode.values.asNameMap()[prefs.getString(_modeKey)] ?? ThemeMode.dark,
        accent: accent != null ? Color(accent) : null,
      );
    } catch (_) {
    }
  }

  Future<void> update(ThemeSettings next) async {
    settings.value = next;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_presetKey, next.presetId);
    await prefs.setString(_modeKey, next.mode.name);
    if (next.accent != null) {
      await prefs.setInt(_accentKey, next.accent!.toARGB32());
    } else {
      await prefs.remove(_accentKey);
    }
  }
}
