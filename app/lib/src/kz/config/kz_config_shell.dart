import 'package:droiddeck/src/kz/config/apps_config_page.dart';
import 'package:droiddeck/src/kz/kz_appearance_page.dart';
import 'package:droiddeck/src/kz/kz_integrations_page.dart';
import 'package:flutter/material.dart';

/// Configurador do KZ Companion (navegador no próprio PC): aplicativos, integrações e aparência.
class KzConfigShell extends StatefulWidget {
  const KzConfigShell({super.key, this.pages});

  /// Os unicos destinos do configurador do Windows. Buttons, Profiles, o editor do deck antigo e a
  /// importacao do Stream Deck foram removidos de proposito (546ed96): nao voltam por aqui.
  static const destinationLabels = ['Applications', 'Integrations', 'Appearance'];

  /// Só para testes: páginas no lugar das reais (que precisam do PC).
  final List<Widget>? pages;

  @override
  State<KzConfigShell> createState() => _KzConfigShellState();
}

class _KzConfigShellState extends State<KzConfigShell> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final pages = widget.pages ??
        const <Widget>[
          AppsConfigPage(),
          KzIntegrationsPage(),
          KzAppearancePage(),
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
            NavigationRailDestination(icon: Icon(Icons.extension_outlined), label: Text('Integrations')),
            NavigationRailDestination(icon: Icon(Icons.palette_outlined), label: Text('Appearance')),
          ],
        ),
        VerticalDivider(width: 1, color: scheme.outlineVariant),
        Expanded(child: IndexedStack(index: _index, children: pages)),
      ]),
    );
  }
}
