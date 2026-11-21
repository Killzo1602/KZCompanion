import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/config/appearance_settings_page.dart';
import 'package:droiddeck/src/connection/constants.dart';
import 'package:droiddeck/src/home/audio_page.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:droiddeck/src/stream_deck/stream_deck_page.dart';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

/// Preferências de exibição do painel (persistidas no aparelho).
class KzPrefs {
  static const keepAwakeKey = 'kz_keep_awake';
  static const immersiveKey = 'kz_immersive';
  static final keepAwake = signal(true);
  static final immersive = signal(true);

  static Future<void> load() async {
    final sp = await SharedPreferences.getInstance();
    keepAwake.value = sp.getBool(keepAwakeKey) ?? true;
    immersive.value = sp.getBool(immersiveKey) ?? true;
  }

  static Future<void> setKeepAwake(bool v) async {
    keepAwake.value = v;
    (await SharedPreferences.getInstance()).setBool(keepAwakeKey, v);
  }

  static Future<void> setImmersive(bool v) async {
    immersive.value = v;
    (await SharedPreferences.getInstance()).setBool(immersiveKey, v);
  }
}

class KzSettingsPage extends StatefulWidget {
  const KzSettingsPage({super.key, required this.c});
  final KzController c;

  @override
  State<KzSettingsPage> createState() => _KzSettingsPageState();
}

class _KzSettingsPageState extends State<KzSettingsPage> {
  String _ip = '';
  String _port = '';

  @override
  void initState() {
    super.initState();
    SharedPreferences.getInstance().then((sp) {
      if (!mounted) return;
      setState(() {
        _ip = sp.getString(Constants.ipAddrKey) ?? '';
        _port = sp.getString(Constants.portKey) ?? Constants.defaultPort;
      });
    });
  }

  @override
  Widget build(BuildContext context) {
    final c = widget.c;
    final theme = ThemeController.instance;
    return ListView(padding: const EdgeInsets.all(12), children: [
      Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Expanded(
          child: Column(children: [
            KzCard(
              title: 'Accent color',
              icon: Icons.palette_outlined,
              child: Watch((context) {
                final s = theme.settings.value;
                return Wrap(spacing: 10, runSpacing: 10, children: [
                  for (final color in kAccentChoices)
                    GestureDetector(
                      onTap: () => theme.update(s.copyWith(accent: () => color)),
                      child: Container(
                        width: 36,
                        height: 36,
                        decoration: BoxDecoration(
                          color: color,
                          shape: BoxShape.circle,
                          border: Border.all(
                            color: s.effectiveAccent.toARGB32() == color.toARGB32()
                                ? Colors.white
                                : Colors.transparent,
                            width: 2.5,
                          ),
                        ),
                      ),
                    ),
                ]);
              }),
            ),
            const SizedBox(height: 12),
            KzCard(
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
            ),
          ]),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Column(children: [
            KzCard(
              title: 'Connected PC',
              icon: Icons.computer_rounded,
              child: Watch((context) {
                final status = c.signalR.connectionStatus.value;
                return Column(children: [
                  KzMetricRow('Address', _ip.isEmpty ? 'Not paired' : '$_ip:$_port', available: _ip.isNotEmpty),
                  KzMetricRow('Status', status == 'Connected' ? 'Connected' : status),
                  KzMetricRow('Link', c.signalR.currentUrl.value.contains('127.0.0.1') ? 'USB cable' : 'Wi-Fi / LAN'),
                  const SizedBox(height: 8),
                  Row(children: [
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: () => Navigator.of(context).pushNamed('/config'),
                        icon: const Icon(Icons.qr_code_scanner_rounded, size: 18),
                        label: const Text('Pair / change PC'),
                      ),
                    ),
                  ]),
                ]);
              }),
            ),
            const SizedBox(height: 12),
            KzCard(
              title: 'More',
              icon: Icons.tune_rounded,
              padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
              child: Column(children: [
                _link(context, Icons.grid_view_rounded, 'Button deck', () => _push(context, (ctx) => _Wrapped(
                      title: 'Button deck',
                      child: StreamDeckPage(onToggleFullscreen: () {}, isFullscreen: false),
                    ))),
                _link(context, Icons.tune_rounded, 'Audio mixer', () => _push(context, (ctx) => _Wrapped(
                      title: 'Audio mixer',
                      child: AudioPage(controller: c.home, isFullscreen: false, onToggleFullscreen: () {}),
                    ))),
                _link(context, Icons.color_lens_outlined, 'Full appearance settings',
                    () => _push(context, (_) => const AppearanceSettingsPage())),
              ]),
            ),
            const SizedBox(height: 12),
            const KzCard(
              title: 'About',
              icon: Icons.info_outline_rounded,
              child: Text(
                'KZ Companion by KZ Dev Studio.\nBased on DroidDeck (Apache License 2.0) by Gabriel Freitas.',
                style: TextStyle(fontSize: 12, height: 1.4),
              ),
            ),
          ]),
        ),
      ]),
    ]);
  }

  void _push(BuildContext context, WidgetBuilder builder) =>
      Navigator.of(context).push(MaterialPageRoute(builder: builder));

  Widget _link(BuildContext context, IconData icon, String label, VoidCallback onTap) => ListTile(
        dense: true,
        leading: Icon(icon, size: 20),
        title: Text(label),
        trailing: const Icon(Icons.chevron_right_rounded),
        onTap: onTap,
      );
}

class _Wrapped extends StatelessWidget {
  const _Wrapped({required this.title, required this.child});
  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: Text(title), toolbarHeight: 44),
        body: SafeArea(top: false, child: child),
      );
}
