import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

/// Aparência do KZ Companion. Tema, modo e cor de destaque valem em qualquer tela (inclusive
/// no configurador do PC); as opções do painel (fundo, espaçamento, cantos, seções) ficam
/// no celular, então só aparecem no app.
class KzAppearancePage extends StatelessWidget {
  const KzAppearancePage({super.key});

  @override
  Widget build(BuildContext context) {
    final onDevice = !WebMode.configurator;
    return Scaffold(
      appBar: AppBar(title: const Text('Appearance'), toolbarHeight: 44),
      body: SafeArea(
        top: false,
        child: LayoutBuilder(builder: (context, box) {
          final theme = _ThemeCard();
          final rest = <Widget>[
            if (onDevice) const _DashboardCard(),
            if (onDevice) const _DisplayCard(),
          ];
          if (!onDevice) {
            return Align(
              alignment: Alignment.topCenter,
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 640),
                child: ListView(padding: const EdgeInsets.all(16), children: [
                  theme,
                  const SizedBox(height: 12),
                  Text(
                    'Dashboard options (background, spacing, corners and sections) are set on the phone: '
                    'Settings → Appearance in the Android app.',
                    style: TextStyle(
                        fontSize: 12, color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6)),
                  ),
                ]),
              ),
            );
          }
          if (box.maxWidth < 620) {
            return ListView(padding: const EdgeInsets.all(12), children: [
              theme,
              for (final w in rest) ...[const SizedBox(height: 12), w],
            ]);
          }
          return ListView(padding: const EdgeInsets.all(12), children: [
            Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Expanded(child: theme),
              const SizedBox(width: 12),
              Expanded(
                child: Column(children: [
                  for (var i = 0; i < rest.length; i++) ...[if (i > 0) const SizedBox(height: 12), rest[i]],
                ]),
              ),
            ]),
          ]);
        }),
      ),
    );
  }
}

class _Label extends StatelessWidget {
  const _Label(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 12, bottom: 6),
        child: Text(text,
            style: TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
                color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.7))),
      );
}

class _ThemeCard extends StatelessWidget {
  final _ctrl = ThemeController.instance;

  @override
  Widget build(BuildContext context) {
    return KzCard(
      title: 'Theme',
      icon: Icons.palette_outlined,
      child: Watch((context) {
        final s = _ctrl.settings.value;
        final scheme = Theme.of(context).colorScheme;
        return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const _Label('Style'),
          Wrap(spacing: 8, runSpacing: 8, children: [
            for (final p in kThemePresets)
              ChoiceChip(
                label: Text(p.name),
                selected: s.preset.id == p.id,
                onSelected: (_) => _ctrl.update(s.copyWith(presetId: p.id)),
              ),
          ]),
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Text(s.preset.description,
                style: TextStyle(fontSize: 11.5, color: scheme.onSurface.withValues(alpha: 0.55))),
          ),
          const _Label('Mode'),
          SegmentedButton<ThemeMode>(
            showSelectedIcon: false,
            segments: const [
              ButtonSegment(value: ThemeMode.dark, label: Text('Dark')),
              ButtonSegment(value: ThemeMode.light, label: Text('Light')),
              ButtonSegment(value: ThemeMode.system, label: Text('System')),
            ],
            selected: {s.mode},
            onSelectionChanged: (v) => _ctrl.update(s.copyWith(mode: v.first)),
          ),
          const _Label('Accent color'),
          Wrap(spacing: 10, runSpacing: 10, children: [
            for (final color in kAccentChoices)
              GestureDetector(
                onTap: () => _ctrl.update(s.copyWith(accent: () => color)),
                child: Container(
                  width: 34,
                  height: 34,
                  decoration: BoxDecoration(
                    color: color,
                    shape: BoxShape.circle,
                    border: Border.all(
                      color: s.effectiveAccent.toARGB32() == color.toARGB32()
                          ? scheme.onSurface
                          : Colors.transparent,
                      width: 2.5,
                    ),
                  ),
                ),
              ),
          ]),
        ]);
      }),
    );
  }
}

class _DashboardCard extends StatelessWidget {
  const _DashboardCard();

  @override
  Widget build(BuildContext context) {
    return KzCard(
      title: 'Dashboard',
      icon: Icons.dashboard_customize_outlined,
      child: Watch((context) {
        return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const _Label('Background'),
          SegmentedButton<KzBackground>(
            showSelectedIcon: false,
            segments: [for (final b in KzBackground.values) ButtonSegment(value: b, label: Text(b.label))],
            selected: {KzPrefs.background.value},
            onSelectionChanged: (v) => KzPrefs.setBackground(v.first),
          ),
          const _Label('Card spacing'),
          SegmentedButton<KzDensity>(
            showSelectedIcon: false,
            segments: [for (final d in KzDensity.values) ButtonSegment(value: d, label: Text(d.label))],
            selected: {KzPrefs.density.value},
            onSelectionChanged: (v) => KzPrefs.setDensity(v.first),
          ),
          const _Label('Card corners'),
          SegmentedButton<KzCorners>(
            showSelectedIcon: false,
            segments: [for (final c in KzCorners.values) ButtonSegment(value: c, label: Text(c.label))],
            selected: {KzPrefs.corners.value},
            onSelectionChanged: (v) => KzPrefs.setCorners(v.first),
          ),
          const _Label('Sections'),
          _switch('CPU / GPU / RAM', KzPrefs.showStats.value, KzPrefs.setShowStats),
          _switch('Favorites', KzPrefs.showFavorites.value, KzPrefs.setShowFavorites),
          _switch('Audio controls and mixer', KzPrefs.showAudio.value, KzPrefs.setShowAudio),
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton.icon(
              onPressed: KzPrefs.resetDashboard,
              icon: const Icon(Icons.restart_alt_rounded, size: 18),
              label: const Text('Restore default layout'),
            ),
          ),
        ]);
      }),
    );
  }

  Widget _switch(String title, bool value, ValueChanged<bool> onChanged) => SwitchListTile(
        dense: true,
        contentPadding: EdgeInsets.zero,
        title: Text(title),
        value: value,
        onChanged: onChanged,
      );
}

class _DisplayCard extends StatelessWidget {
  const _DisplayCard();

  @override
  Widget build(BuildContext context) {
    return KzCard(
      title: 'Display',
      icon: Icons.stay_current_landscape_rounded,
      padding: const EdgeInsets.fromLTRB(14, 8, 8, 8),
      child: Column(children: [
        Watch((context) => SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Keep screen on'),
              value: KzPrefs.keepAwake.value,
              onChanged: (v) {
                KzPrefs.setKeepAwake(v);
                WakelockPlus.toggle(enable: v);
              },
            )),
        Watch((context) => SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Hide system bars'),
              value: KzPrefs.immersive.value,
              onChanged: KzPrefs.setImmersive,
            )),
      ]),
    );
  }
}
