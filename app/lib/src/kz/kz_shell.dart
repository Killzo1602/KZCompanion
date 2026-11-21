import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/apps_page.dart';
import 'package:droiddeck/src/kz/controls_page.dart';
import 'package:droiddeck/src/kz/dashboard_page.dart';
import 'package:droiddeck/src/kz/hardware_page.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_settings_page.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

/// Painel do KZ Companion no Android: cinco seções, pensado para o celular deitado.
class KzShell extends StatefulWidget {
  const KzShell({super.key});

  @override
  State<KzShell> createState() => _KzShellState();
}

class _KzShellState extends State<KzShell> with WidgetsBindingObserver {
  static const _tabKey = 'kz_tab';
  late final KzController c;
  int _index = 0;
  void Function()? _prefsDispose;

  static const _items = <(IconData, String)>[
    (Icons.dashboard_rounded, 'Dashboard'),
    (Icons.apps_rounded, 'Apps'),
    (Icons.monitor_heart_rounded, 'Hardware'),
    (Icons.settings_remote_rounded, 'Controls'),
    (Icons.settings_rounded, 'Settings'),
  ];

  @override
  void initState() {
    super.initState();
    c = KzController();
    WidgetsBinding.instance.addObserver(this);
    SystemChrome.setPreferredOrientations(
        [DeviceOrientation.landscapeLeft, DeviceOrientation.landscapeRight]);
    KzPrefs.load().then((_) => WakelockPlus.toggle(enable: KzPrefs.keepAwake.value));
    _prefsDispose = effect(() {
      final immersive = KzPrefs.immersive.value;
      SystemChrome.setEnabledSystemUIMode(
          immersive ? SystemUiMode.immersiveSticky : SystemUiMode.edgeToEdge);
    });
    SharedPreferences.getInstance().then((sp) {
      final saved = sp.getInt(_tabKey);
      if (saved != null && saved >= 0 && saved < _items.length && mounted) {
        setState(() => _index = saved);
      }
    });
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Tela ligou / voltou de outro app: confere a conexao na hora em vez de esperar.
    if (state == AppLifecycleState.resumed) {
      c.signalR.onAppResumed();
      c.refreshPcInfo();
      c.audio.refresh();
    } else if (state == AppLifecycleState.paused) {
      c.signalR.diag('app paused');
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _prefsDispose?.call();
    c.dispose();
    SystemChrome.setPreferredOrientations(DeviceOrientation.values);
    SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
    WakelockPlus.disable();
    super.dispose();
  }

  void _go(int i) {
    setState(() => _index = i);
    SharedPreferences.getInstance().then((sp) => sp.setInt(_tabKey, i));
  }

  Widget _page() => switch (_index) {
        0 => DashboardPage(c: c, onOpenApps: () => _go(1)),
        1 => AppsPage(c: c),
        2 => HardwarePage(c: c),
        3 => ControlsPage(c: c),
        _ => KzSettingsPage(c: c),
      };

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      // O recorte (notch) fica nas laterais com o celular deitado: o SafeArea cuida.
      body: SafeArea(
        child: Row(children: [
          NavigationRail(
            selectedIndex: _index,
            onDestinationSelected: _go,
            labelType: NavigationRailLabelType.all,
            minWidth: 72,
            backgroundColor: Colors.transparent,
            indicatorColor: scheme.primary.withValues(alpha: 0.18),
            destinations: [
              for (final (icon, label) in _items)
                NavigationRailDestination(
                  icon: Icon(icon),
                  label: Text(label, style: const TextStyle(fontSize: 11)),
                  padding: const EdgeInsets.symmetric(vertical: 2),
                ),
            ],
          ),
          VerticalDivider(width: 1, color: scheme.outlineVariant),
          Expanded(
            child: Column(children: [
              _LinkHeader(c: c),
              _AuthBanner(c: c),
              Expanded(
                child: AnimatedSwitcher(
                  duration: const Duration(milliseconds: 160),
                  child: KeyedSubtree(key: ValueKey(_index), child: _page()),
                ),
              ),
            ]),
          ),
        ]),
      ),
    );
  }
}

/// Cabecalho compacto: nome do PC, Windows e um indicador pequeno Online / Reconnecting / Offline.
class _LinkHeader extends StatelessWidget {
  const _LinkHeader({required this.c});
  final KzController c;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Watch((context) {
      final state = c.signalR.linkState.value;
      final name = c.signalR.pcName.value;
      final os = c.signalR.pcOs.value;
      final (Color color, String label) = switch (state) {
        'online' => (const Color(0xFF4ADE80), 'Online'),
        'offline' => (const Color(0xFFEF5350), 'Offline'),
        _ => (const Color(0xFFFBBF24), 'Reconnecting'),
      };
      return SizedBox(
        height: 30,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14),
          child: Row(children: [
            Text(name.isEmpty ? 'PC' : name,
                style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700, letterSpacing: 0.4)),
            if (os.isNotEmpty) ...[
              const SizedBox(width: 8),
              Text(os, style: TextStyle(fontSize: 12, color: scheme.onSurface.withValues(alpha: 0.55))),
            ],
            const Spacer(),
            Container(width: 8, height: 8, decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
            const SizedBox(width: 6),
            Text(label, style: TextStyle(fontSize: 12, color: color, fontWeight: FontWeight.w600)),
          ]),
        ),
      );
    });
  }
}

/// O PC recusou a chave (reinstalou / outro PC): leva ao pareamento em vez de falhar calado.
class _AuthBanner extends StatelessWidget {
  const _AuthBanner({required this.c});
  final KzController c;

  @override
  Widget build(BuildContext context) {
    return Watch((context) {
      final err = '${c.home.error.value ?? ''}${c.appsError.value ?? ''}';
      if (!(err.contains('401') || err.contains('403'))) return const SizedBox.shrink();
      return Material(
        color: const Color(0xFF3A1D1D),
        child: ListTile(
          dense: true,
          leading: const Icon(Icons.link_off_rounded, color: Color(0xFFEF5350)),
          title: const Text('This PC does not recognize this device. Pair again.'),
          trailing: TextButton(
            onPressed: () => Navigator.of(context).pushReplacementNamed('/config'),
            child: const Text('Pair'),
          ),
        ),
      );
    });
  }
}
