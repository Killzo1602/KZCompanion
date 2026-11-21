import 'package:droiddeck/src/config/appearance_settings_page.dart';
import 'package:droiddeck/src/kz/config/apps_config_page.dart';
import 'package:droiddeck/src/stream_deck/stream_deck_page.dart';
import 'package:flutter/material.dart';

/// Configurador do KZ Companion (navegador no próprio PC): aplicativos, botões e aparência.
class KzConfigShell extends StatefulWidget {
  const KzConfigShell({super.key});

  @override
  State<KzConfigShell> createState() => _KzConfigShellState();
}

class _KzConfigShellState extends State<KzConfigShell> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final pages = <Widget>[
      const AppsConfigPage(),
      StreamDeckPage(onToggleFullscreen: () {}, isFullscreen: false),
      const AppearanceSettingsPage(),
    ];
    return Scaffold(
      body: Row(children: [
        NavigationRail(
          selectedIndex: _index,
          onDestinationSelected: (i) => setState(() => _index = i),
          labelType: NavigationRailLabelType.all,
          leading: Padding(
            padding: const EdgeInsets.symmetric(vertical: 12),
            child: Column(children: [
              Icon(Icons.bolt_rounded, color: scheme.primary, size: 28),
              const SizedBox(height: 4),
              const Text('KZ Companion', style: TextStyle(fontSize: 11, fontWeight: FontWeight.w600)),
            ]),
          ),
          destinations: const [
            NavigationRailDestination(icon: Icon(Icons.apps_rounded), label: Text('Applications')),
            NavigationRailDestination(icon: Icon(Icons.grid_view_rounded), label: Text('Buttons')),
            NavigationRailDestination(icon: Icon(Icons.palette_outlined), label: Text('Appearance')),
          ],
        ),
        VerticalDivider(width: 1, color: scheme.outlineVariant),
        Expanded(child: IndexedStack(index: _index, children: pages)),
      ]),
    );
  }
}
