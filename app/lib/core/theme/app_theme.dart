import 'package:flutter/material.dart';

/// Tema nomeado do KZ Companion: define o visual escuro (fundo e superfícies). O claro
/// deriva da cor de destaque.
@immutable
class ThemePreset {
  const ThemePreset({
    required this.id,
    required this.name,
    required this.description,
    required this.accent,
    required this.background,
    required this.surface,
    required this.outline,
  });

  final String id;
  final String name;
  final String description;
  final Color accent;
  final Color background;
  final Color surface;
  final Color outline;
}

/// Ciano do KZ Companion: o destaque padrão.
const kKzCyan = Color(0xFF2DD4FF);

const kThemePresets = <ThemePreset>[
  ThemePreset(
    id: 'kz',
    name: 'Graphite',
    description: 'Default KZ Companion look',
    accent: kKzCyan,
    background: Color(0xFF0E1013),
    surface: Color(0xFF16191E),
    outline: Color(0xFFD8DEE6),
  ),
  ThemePreset(
    id: 'oled',
    name: 'Black',
    description: 'True black for OLED screens',
    accent: kKzCyan,
    background: Color(0xFF000000),
    surface: Color(0xFF0D0D0D),
    outline: Color(0xFF8A8A8A),
  ),
];

/// Cores oferecidas para trocar o destaque do tema.
const kAccentChoices = <Color>[
  kKzCyan, // ciano KZ
  Color(0xFF42A5F5), // azul
  Color(0xFF26A69A), // verde-água
  Color(0xFF66BB6A), // verde
  Color(0xFFFFB300), // âmbar
  Color(0xFFFF7043), // laranja
  Color(0xFFEF5350), // vermelho
  Color(0xFFEC407A), // rosa
  Color(0xFFAB47BC), // roxo
  Color(0xFF7E57C2), // violeta
];

/// Temas antigos (Clássico, Neon, Âmbar, Synthwave...) caem no padrão do KZ.
ThemePreset presetById(String? id) => kThemePresets.firstWhere(
  (p) => p.id == id,
  orElse: () => kThemePresets.first,
);

/// Escolhas do usuário. O destaque nulo segue o tema escolhido.
@immutable
class ThemeSettings {
  const ThemeSettings({
    this.presetId = 'kz',
    this.mode = ThemeMode.dark,
    this.accent,
  });

  final String presetId;
  final ThemeMode mode;
  final Color? accent;

  ThemePreset get preset => presetById(presetId);
  Color get effectiveAccent => accent ?? preset.accent;

  ThemeSettings copyWith({
    String? presetId,
    ThemeMode? mode,
    Color? Function()? accent,
  }) {
    return ThemeSettings(
      presetId: presetId ?? this.presetId,
      mode: mode ?? this.mode,
      accent: accent != null ? accent() : this.accent,
    );
  }
}

Color _onColor(Color c) =>
    c.computeLuminance() > 0.45 ? Colors.black : Colors.white;

ThemeData buildAppTheme(ThemeSettings s, Brightness brightness) {
  final preset = s.preset;
  final accent = s.effectiveAccent;
  final dark = brightness == Brightness.dark;

  // No tema claro, um destaque muito claro vira texto ilegível sobre branco:
  // escurece até ter contraste, mantendo o tom.
  final primary = !dark && accent.computeLuminance() > 0.4
      ? HSLColor.fromColor(accent).withLightness(0.30).toColor()
      : accent;
  var scheme = ColorScheme.fromSeed(
    seedColor: accent,
    brightness: brightness,
  ).copyWith(primary: primary, onPrimary: _onColor(primary));

  Color scaffold;
  if (dark) {
    // Superfícies do tema, em degraus a partir do fundo.
    Color step(double t) => Color.lerp(preset.background, preset.outline, t)!;
    scheme = scheme.copyWith(
      surface: preset.surface,
      surfaceContainerLowest: preset.background,
      surfaceContainerLow: step(0.04),
      surfaceContainer: step(0.07),
      surfaceContainerHigh: step(0.10),
      surfaceContainerHighest: step(0.14),
      outlineVariant: preset.outline.withValues(alpha: 0.16),
    );
    scaffold = preset.background;
  } else {
    scaffold = scheme.surface;
  }

  // Controles com cantos de 12 px (DESIGN.md: "control"), não pílulas.
  final controlShape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(12));
  final controlStyle = ButtonStyle(shape: WidgetStatePropertyAll(controlShape));

  return ThemeData(
    colorScheme: scheme,
    scaffoldBackgroundColor: scaffold,
    appBarTheme: AppBarTheme(
      backgroundColor: scaffold,
      surfaceTintColor: Colors.transparent,
    ),
    filledButtonTheme: FilledButtonThemeData(style: controlStyle),
    elevatedButtonTheme: ElevatedButtonThemeData(style: controlStyle),
    outlinedButtonTheme: OutlinedButtonThemeData(style: controlStyle),
    // Aviso de baixo: superfície do painel com borda fina, ação no destaque, flutuando.
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: dark ? preset.surface : scheme.inverseSurface,
      contentTextStyle: TextStyle(color: dark ? const Color(0xFFF4F4F5) : scheme.onInverseSurface),
      actionTextColor: dark ? accent : scheme.inversePrimary,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: dark ? BorderSide(color: preset.outline.withValues(alpha: 0.16)) : BorderSide.none,
      ),
      insetPadding: const EdgeInsets.fromLTRB(16, 0, 88, 16),
    ),
    // Foco do teclado visível no configurador (o padrão some no quase-preto).
    focusColor: accent.withValues(alpha: dark ? 0.28 : 0.20),
  );
}

/// builder do MaterialApp: em tela larga (configurador no navegador), o aviso de baixo
/// tem largura de aviso, centralizado, em vez de uma faixa de ponta a ponta.
Widget narrowSnackBars(BuildContext context, Widget? child) {
  if (MediaQuery.sizeOf(context).width < 640) return child!;
  final t = Theme.of(context);
  return Theme(
    data: t.copyWith(snackBarTheme: t.snackBarTheme.copyWith(width: 480)),
    child: child!,
  );
}
