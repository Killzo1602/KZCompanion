import 'package:droiddeck/core/core.dart';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Fundo do painel.
enum KzBackground {
  flat('Flat', 'Plain graphite'),
  glow('Glow', 'Soft accent glow at the top');

  const KzBackground(this.label, this.description);
  final String label;
  final String description;
}

/// Espaçamento do painel: quanto respiro os cartões têm.
enum KzDensity {
  comfortable('Comfortable', 1.0, 12.0),
  compact('Compact', 0.72, 8.0);

  const KzDensity(this.label, this.padScale, this.gap);
  final String label;

  /// Multiplica o padding interno dos cartões.
  final double padScale;

  /// Espaço entre os cartões do painel.
  final double gap;
}

/// Cantos dos cartões.
enum KzCorners {
  sharp('Sharp', 8),
  standard('Standard', 16),
  round('Round', 24);

  const KzCorners(this.label, this.radius);
  final String label;
  final double radius;
}

/// Preferências de exibição do KZ Companion (persistidas no aparelho). O tema (preset,
/// modo e destaque) fica no [ThemeController]; aqui ficam as opções do painel.
class KzPrefs {
  static const keepAwakeKey = 'kz_keep_awake';
  static const immersiveKey = 'kz_immersive';
  static const backgroundKey = 'kz_background';
  static const densityKey = 'kz_density';
  static const cornersKey = 'kz_corners';
  static const showStatsKey = 'kz_show_stats';
  static const showFavoritesKey = 'kz_show_favorites';
  static const showAudioKey = 'kz_show_audio';

  static final keepAwake = signal(true);
  static final immersive = signal(true);
  static final background = signal(KzBackground.flat);
  static final density = signal(KzDensity.comfortable);
  static final corners = signal(KzCorners.standard);
  static final showStats = signal(true);
  static final showFavorites = signal(true);
  static final showAudio = signal(true);

  static T _enum<T extends Enum>(List<T> values, String? name, T fallback) =>
      values.asNameMap()[name] ?? fallback;

  static Future<void> load() async {
    final sp = await SharedPreferences.getInstance();
    keepAwake.value = sp.getBool(keepAwakeKey) ?? true;
    immersive.value = sp.getBool(immersiveKey) ?? true;
    background.value = _enum(KzBackground.values, sp.getString(backgroundKey), KzBackground.flat);
    density.value = _enum(KzDensity.values, sp.getString(densityKey), KzDensity.comfortable);
    corners.value = _enum(KzCorners.values, sp.getString(cornersKey), KzCorners.standard);
    showStats.value = sp.getBool(showStatsKey) ?? true;
    showFavorites.value = sp.getBool(showFavoritesKey) ?? true;
    showAudio.value = sp.getBool(showAudioKey) ?? true;
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

  /// Volta o visual (fundo, espaçamento, cantos e seções) ao padrão do KZ Companion.
  static Future<void> resetDashboard() async {
    await setBackground(KzBackground.flat);
    await setDensity(KzDensity.comfortable);
    await setCorners(KzCorners.standard);
    await setShowStats(true);
    await setShowFavorites(true);
    await setShowAudio(true);
  }
}

/// Fundo do painel conforme a preferência: liso, ou com um brilho suave do destaque no topo.
class KzBackdrop extends StatelessWidget {
  const KzBackdrop({super.key, required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Watch((context) {
      if (KzPrefs.background.value == KzBackground.flat) return child;
      return DecoratedBox(
        decoration: BoxDecoration(
          gradient: RadialGradient(
            center: const Alignment(0, -1.4),
            radius: 1.3,
            colors: [scheme.primary.withValues(alpha: 0.16), scheme.primary.withValues(alpha: 0)],
          ),
        ),
        child: child,
      );
    });
  }
}
