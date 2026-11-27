import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/connection/usb_route.dart';
import 'package:droiddeck/src/kz/ai_usage_page.dart';
import 'package:droiddeck/src/kz/apps_page.dart';
import 'package:droiddeck/src/kz/controls_page.dart';
import 'package:droiddeck/src/kz/dashboard_page.dart';
import 'package:droiddeck/src/kz/dev_page.dart';
import 'package:droiddeck/src/kz/grid/kz_grid_layout.dart';
import 'package:droiddeck/src/kz/hardware_page.dart';
import 'package:droiddeck/src/kz/kz_sessions.dart';
import 'package:droiddeck/src/kz/kz_settings_page.dart';
import 'package:droiddeck/src/kz/kz_temp_alert.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

class KzShell extends StatefulWidget {
  const KzShell({super.key, this.controller});

  final KzController? controller;

  @override
  State<KzShell> createState() => _KzShellState();
}

class _KzShellState extends State<KzShell> with WidgetsBindingObserver {
  static const _tabKey = 'kz_page';
  late final KzController c;
  late final KzTempAlert _heat;
  late final KzSessionWatch _sessions;
  int _index = 0;
  final _pages = PageController();
  int? _target;
  void Function()? _prefsDispose;

  static const _items = <(IconData, String)>[
    (Icons.dashboard_rounded, 'Dashboard'),
    (Icons.apps_rounded, 'Apps'),
    (Icons.monitor_heart_rounded, 'Hardware'),
    (Icons.settings_remote_rounded, 'Controls'),
    (Icons.auto_awesome_rounded, 'AI'),
    (Icons.terminal_rounded, 'Dev'),
    (Icons.settings_rounded, 'Settings'),
  ];

  @override
  void initState() {
    super.initState();
    c = widget.controller ?? KzController();
    _heat = KzTempAlert(c.signalR);
    _sessions = KzSessionWatch(c)..start();
    KzGrid.load();
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
      final saved = _items.indexWhere((e) => e.$2 == sp.getString(_tabKey));
      if (saved > 0 && mounted) _go(saved, animate: false);
    });
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
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
    _heat.dispose();
    _sessions.dispose();
    KzGrid.editing.value = false;
    c.dispose();
    _pages.dispose();
    SystemChrome.setPreferredOrientations(DeviceOrientation.values);
    SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
    WakelockPlus.disable();
    super.dispose();
  }

  void _go(int i, {bool animate = true}) {
    if (!_pages.hasClients) return;
    final near = (i - _index).abs() == 1;
    _target = i;
    _select(i);
    if (animate && near) {
      _pages
          .animateToPage(i, duration: const Duration(milliseconds: 220), curve: Curves.easeOutCubic)
          .whenComplete(() => _target = null);
    } else {
      _pages.jumpToPage(i);
      _target = null;
    }
  }

  void _select(int i) {
    if (i == _index) return;
    KzGrid.editing.value = false;
    setState(() => _index = i);
    SharedPreferences.getInstance().then((sp) => sp.setString(_tabKey, _items[i].$2));
  }

  Widget _rail(ColorScheme scheme) => LayoutBuilder(
        builder: (context, box) => SingleChildScrollView(
          child: ConstrainedBox(
            constraints: BoxConstraints(minHeight: box.maxHeight),
            child: IntrinsicHeight(
              child: NavigationRail(
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
                      padding: EdgeInsets.zero,
                    ),
                ],
              ),
            ),
          ),
        ),
      );

  Widget _dots(ColorScheme scheme) => Row(mainAxisSize: MainAxisSize.min, children: [
        for (var i = 0; i < _items.length; i++)
          Semantics(
            button: true,
            label: _items[i].$2,
            child: GestureDetector(
              key: ValueKey('dot-$i'),
              behavior: HitTestBehavior.opaque,
              onTap: () => _go(i),
              child: SizedBox(
                width: 30,
                height: 26,
                child: Center(
                  child: AnimatedContainer(
                    duration: const Duration(milliseconds: 180),
                    width: i == _index ? 20 : 7,
                    height: 7,
                    decoration: BoxDecoration(
                      color: scheme.onSurface.withValues(alpha: i == _index ? 0.85 : 0.25),
                      borderRadius: BorderRadius.circular(4),
                    ),
                  ),
                ),
              ),
            ),
          ),
      ]);

  Future<void> _jump() async {
    final i = await showModalBottomSheet<int>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => ListView(shrinkWrap: true, children: [
        for (var i = 0; i < _items.length; i++)
          ListTile(
            dense: true,
            selected: i == _index,
            leading: Icon(_items[i].$1, size: 20),
            title: Text(_items[i].$2),
            onTap: () => Navigator.pop(ctx, i),
          ),
      ]),
    );
    if (i != null && mounted) _go(i);
  }

  Widget _page(int i) => switch (i) {
        0 => DashboardPage(c: c, onOpenApps: () => _go(1)),
        1 => AppsPage(c: c),
        2 => HardwarePage(c: c),
        3 => ControlsPage(c: c),
        4 => AiUsagePage(c: c, sessions: _sessions),
        5 => DevPage(c: c),
        _ => KzSettingsPage(c: c),
      };

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      body: KzBackdrop(
        child: SafeArea(
        child: Watch((context) {
          final sideBar = KzPrefs.sideBar.value;
          return Row(children: [
          if (sideBar) ...[
            _rail(scheme),
            VerticalDivider(width: 1, color: scheme.outlineVariant),
          ],
          Expanded(
            child: Column(children: [
              _AuthBanner(c: c),
              KzHeatBanner(_heat),
              KzSessionBanner(_sessions, onOpen: () => _go(4)),
              Expanded(
                key: const ValueKey('kz-pages'),
                child: Watch((context) => PageView.builder(
                  controller: _pages,
                  physics: KzGrid.editing.value ? const NeverScrollableScrollPhysics() : null,
                  itemCount: _items.length,
                  onPageChanged: (i) {
                    if (_target == null) _select(i);
                  },
                  itemBuilder: (_, i) => _page(i),
                )),
              ),
              _StatusBar(
                c: c,
                onEdit: _index == 0 ? () => KzGrid.editing.value = true : null,
                section: sideBar ? null : _items[_index].$2,
                onSection: _jump,
                dots: sideBar ? null : _dots(scheme),
              ),
            ]),
          ),
          ]);
        }),
        ),
      ),
    );
  }
}

class _StatusBar extends StatelessWidget {
  const _StatusBar({required this.c, this.onEdit, this.section, this.onSection, this.dots});
  final KzController c;
  final VoidCallback? onEdit;
  final String? section;
  final VoidCallback? onSection;
  final Widget? dots;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final muted = scheme.onSurface.withValues(alpha: 0.55);
    return Watch((context) {
      final state = c.signalR.linkState.value;
      final name = c.signalR.pcName.value;
      final (Color color, String label) = switch (state) {
        'online' => (const Color(0xFF4ADE80), 'Online'),
        'offline' => (const Color(0xFFEF5350), 'Offline'),
        _ => (const Color(0xFFFBBF24), 'Reconnecting'),
      };
      final cable = UsbRoute.isUsb(c.signalR.activeBaseUrl.value);
      final pc = Text(name.isEmpty ? 'PC' : name,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(fontSize: 11.5, fontWeight: FontWeight.w700, color: muted));
      return SizedBox(
        height: 26,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14),
          child: Row(children: [
            Expanded(
              child: section == null
                  ? pc
                  : Row(children: [
                      Flexible(child: pc),
                      const SizedBox(width: 8),
                      InkWell(
                        onTap: onSection,
                        child: Row(mainAxisSize: MainAxisSize.min, children: [
                          Text(section!, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
                          const Icon(Icons.expand_less_rounded, size: 16),
                        ]),
                      ),
                    ]),
            ),
            if (dots != null) dots!,
            Expanded(
              child: Row(mainAxisAlignment: MainAxisAlignment.end, children: [
                if (state == 'online') ...[
                  Icon(cable ? Icons.usb_rounded : Icons.wifi_rounded, size: 13, color: muted),
                  const SizedBox(width: 3),
                  Text(cable ? 'USB' : 'Wi-Fi', style: TextStyle(fontSize: 11.5, color: muted)),
                  const SizedBox(width: 10),
                ],
                Container(width: 7, height: 7, decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
                const SizedBox(width: 5),
                Flexible(
                  child: Text(label,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(fontSize: 11.5, color: color, fontWeight: FontWeight.w600)),
                ),
                if (onEdit != null)
                  InkWell(
                    onTap: onEdit,
                    child: const SizedBox(
                      width: 38,
                      height: 26,
                      child: Icon(Icons.dashboard_customize_rounded, size: 18, semanticLabel: 'Edit dashboard layout'),
                    ),
                  ),
              ]),
            ),
          ]),
        ),
      );
    });
  }
}

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
