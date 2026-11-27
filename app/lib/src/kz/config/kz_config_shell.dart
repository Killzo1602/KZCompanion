import 'package:droiddeck/src/kz/config/apps_config_page.dart';
import 'package:droiddeck/src/kz/kz_appearance_page.dart';
import 'package:droiddeck/src/kz/kz_integrations_page.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:flutter/material.dart';

class KzConfigShell extends StatefulWidget {
  const KzConfigShell({super.key, this.pages});

  static const destinationLabels = ['Applications', 'Integrations', 'Appearance'];

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
    final theme = Theme.of(context);
    return Scaffold(
      body: KzBackdrop(
        child: Theme(
          data: theme.copyWith(
            scaffoldBackgroundColor: Colors.transparent,
            appBarTheme: theme.appBarTheme.copyWith(backgroundColor: Colors.transparent, scrolledUnderElevation: 0),
          ),
          child: Row(children: [
            NavigationRail(
              selectedIndex: _index,
              onDestinationSelected: (i) => setState(() => _index = i),
              labelType: NavigationRailLabelType.all,
              minWidth: 112,
              backgroundColor: scheme.onSurface.withValues(alpha: 0.03),
              indicatorColor: scheme.primary.withValues(alpha: 0.18),
              leading: Padding(
                padding: const EdgeInsets.symmetric(vertical: 16),
                child: Column(children: [
                  Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(14),
                      gradient: const LinearGradient(
                        begin: Alignment.topLeft,
                        end: Alignment.bottomRight,
                        colors: [KzColors.audio, KzColors.ram],
                      ),
                    ),
                    child: const Icon(Icons.bolt_rounded, color: Colors.white, size: 28),
                  ),
                  const SizedBox(height: 8),
                  const Text('KZ Companion', style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700)),
                ]),
              ),
              destinations: const [
                NavigationRailDestination(icon: Icon(Icons.apps_rounded), label: Text('Applications')),
                NavigationRailDestination(icon: Icon(Icons.extension_outlined), label: Text('Integrations')),
                NavigationRailDestination(icon: Icon(Icons.palette_outlined), label: Text('Appearance')),
              ],
            ),
            VerticalDivider(width: 1, color: scheme.onSurface.withValues(alpha: 0.08)),
            Expanded(child: IndexedStack(index: _index, children: pages)),
          ]),
        ),
      ),
    );
  }
}
