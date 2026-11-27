import 'package:droiddeck/core/core.dart';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

enum KzBackground {
  flat('Flat', 'Plain graphite'),
  glow('Glow', 'Soft accent glow at the top');

  const KzBackground(this.label, this.description);
  final String label;
  final String description;
}

enum KzDensity {
  comfortable('Comfortable', 1.0, 12.0),
  compact('Compact', 0.72, 8.0);

  const KzDensity(this.label, this.padScale, this.gap);
  final String label;

  final double padScale;

  final double gap;
}

enum KzCorners {
  sharp('Sharp', 8),
  standard('Standard', 16),
  round('Round', 24);

  const KzCorners(this.label, this.radius);
  final String label;
  final double radius;
}

class KzPrefs {
  static const keepAwakeKey = 'kz_keep_awake';
  static const immersiveKey = 'kz_immersive';
  static const backgroundKey = 'kz_background';
  static const densityKey = 'kz_density';
  static const cornersKey = 'kz_corners';
  static const showStatsKey = 'kz_show_stats';
  static const showFavoritesKey = 'kz_show_favorites';
  static const showAudioKey = 'kz_show_audio';
  static const showRunningAppsKey = 'kz_show_running_apps';

  static final keepAwake = signal(true);
  static final immersive = signal(true);
  static final background = signal(KzBackground.glow);
  static final sideBar = signal(false);
  static const sideBarKey = 'kz_side_bar';
  static final density = signal(KzDensity.comfortable);
  static final corners = signal(KzCorners.standard);
  static final showStats = signal(true);
  static final showFavorites = signal(true);
  static final showAudio = signal(true);
  static final showRunningApps = signal(true);

  static T _enum<T extends Enum>(List<T> values, String? name, T fallback) =>
      values.asNameMap()[name] ?? fallback;

  static Future<void> load() async {
    final sp = await SharedPreferences.getInstance();
    keepAwake.value = sp.getBool(keepAwakeKey) ?? true;
    immersive.value = sp.getBool(immersiveKey) ?? true;
    background.value = _enum(KzBackground.values, sp.getString(backgroundKey), KzBackground.glow);
    density.value = _enum(KzDensity.values, sp.getString(densityKey), KzDensity.comfortable);
    corners.value = _enum(KzCorners.values, sp.getString(cornersKey), KzCorners.standard);
    showStats.value = sp.getBool(showStatsKey) ?? true;
    showFavorites.value = sp.getBool(showFavoritesKey) ?? true;
    showAudio.value = sp.getBool(showAudioKey) ?? true;
    showRunningApps.value = sp.getBool(showRunningAppsKey) ?? true;
    sideBar.value = sp.getBool(sideBarKey) ?? false;
  }

  static Future<void> setKeepAwake(bool v) async {
    keepAwake.value = v;
    (await SharedPreferences.getInstance()).setBool(keepAwakeKey, v);
  }

  static Future<void> setImmersive(bool v) async {
    immersive.value = v;
    (await SharedPreferences.getInstance()).setBool(immersiveKey, v);
  }

  static Future<void> setBackground(KzBackground v) async {
    background.value = v;
    (await SharedPreferences.getInstance()).setString(backgroundKey, v.name);
  }

  static Future<void> setDensity(KzDensity v) async {
    density.value = v;
    (await SharedPreferences.getInstance()).setString(densityKey, v.name);
  }

  static Future<void> setCorners(KzCorners v) async {
    corners.value = v;
    (await SharedPreferences.getInstance()).setString(cornersKey, v.name);
  }

  static Future<void> setShowStats(bool v) async {
    showStats.value = v;
    (await SharedPreferences.getInstance()).setBool(showStatsKey, v);
  }

  static Future<void> setShowFavorites(bool v) async {
    showFavorites.value = v;
    (await SharedPreferences.getInstance()).setBool(showFavoritesKey, v);
  }

  static Future<void> setShowAudio(bool v) async {
    showAudio.value = v;
    (await SharedPreferences.getInstance()).setBool(showAudioKey, v);
  }

  static Future<void> setShowRunningApps(bool v) async {
    showRunningApps.value = v;
    (await SharedPreferences.getInstance()).setBool(showRunningAppsKey, v);
  }

  static Future<void> setSideBar(bool v) async {
    sideBar.value = v;
    (await SharedPreferences.getInstance()).setBool(sideBarKey, v);
  }

  static Future<void> resetDashboard() async {
    await setBackground(KzBackground.glow);
    await setSideBar(false);
    await setDensity(KzDensity.comfortable);
    await setCorners(KzCorners.standard);
    await setShowStats(true);
    await setShowFavorites(true);
    await setShowAudio(true);
    await setShowRunningApps(true);
  }
}

class KzBackdrop extends StatelessWidget {
  const KzBackdrop({super.key, required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Watch((context) {
      if (KzPrefs.background.value == KzBackground.flat) return child;
      Widget glow(Alignment center, Color color, double alpha, Widget child) => DecoratedBox(
            decoration: BoxDecoration(
              gradient: RadialGradient(
                center: center,
                radius: 1.1,
                colors: [color.withValues(alpha: alpha), color.withValues(alpha: 0)],
              ),
            ),
            child: child,
          );
      return glow(
        const Alignment(-0.6, -1.3),
        scheme.primary,
        0.18,
        glow(const Alignment(1.1, 1.2), const Color(0xFF8B5CF6), 0.14, child),
      );
    });
  }
}
