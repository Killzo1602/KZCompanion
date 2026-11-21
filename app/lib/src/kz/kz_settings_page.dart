import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/connection/constants.dart';
import 'package:droiddeck/src/kz/kz_appearance_page.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_integrations_page.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

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
    return ListView(padding: const EdgeInsets.all(12), children: [
      Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Expanded(
          child: Column(children: [
            KzCard(
              title: 'Connected PC',
              icon: Icons.computer_rounded,
              child: Watch((context) {
                final status = c.signalR.linkState.value;
                final diag = c.signalR.diagLog.value;
                return Column(children: [
                  KzMetricRow('Address', _ip.isEmpty ? 'Not paired' : '$_ip:$_port', available: _ip.isNotEmpty),
                  KzMetricRow('Status', status == 'online' ? 'Online' : status == 'offline' ? 'Offline' : 'Reconnecting'),
                  KzMetricRow('Link', c.signalR.currentUrl.value.contains('127.0.0.1') ? 'USB cable' : 'Wi-Fi / LAN'),
                  const SizedBox(height: 6),
                  ExpansionTile(
                    tilePadding: EdgeInsets.zero,
                    dense: true,
                    title: const Text('Connection log', style: TextStyle(fontSize: 12)),
                    children: [
                      Container(
                        constraints: const BoxConstraints(maxHeight: 120),
                        width: double.infinity,
                        child: SingleChildScrollView(
                          reverse: true,
                          child: Text(
                            diag.isEmpty ? 'No events yet.' : diag.join('\n'),
                            style: const TextStyle(fontSize: 10.5, fontFamily: 'monospace', height: 1.35),
                          ),
                        ),
                      ),
                      const Padding(
                        padding: EdgeInsets.symmetric(vertical: 6),
                        child: Text(
                          'Huawei/EMUI: Settings, Battery, App launch, KZ Companion, Manage manually, '
                          'allow background activity. Keeps the link alive when the screen dims.',
                          style: TextStyle(fontSize: 10.5),
                        ),
                      ),
                    ],
                  ),
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
          ]),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Column(children: [
            KzCard(
              title: 'Settings',
              icon: Icons.tune_rounded,
              padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
              child: Column(children: [
                _link(context, Icons.palette_outlined, 'Appearance', () => _push(context, (_) => const KzAppearancePage())),
                _link(context, Icons.extension_outlined, 'Integrations', () => _push(context, (_) => const KzIntegrationsPage())),
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
