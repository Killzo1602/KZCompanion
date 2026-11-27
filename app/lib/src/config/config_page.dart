import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:droiddeck/src/connection/constants.dart';
import 'package:droiddeck/src/services/server_discovery_service.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/config/pc_switcher.dart';
import 'package:droiddeck/src/connection/pc_store.dart';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'qr_scan_page.dart';
import '../welcome/welcome_page.dart';

class ConfigPage extends StatefulWidget {
  const ConfigPage({super.key});

  @override
  ConfigPageState createState() => ConfigPageState();
}

class ConfigPageState extends State<ConfigPage> {
  final _ipController = TextEditingController();
  bool _isLoading = false;

  @override
  void initState() {
    super.initState();
    _loadIP();
  }

  List<SavedPc> _pcs = [];

  Future<void> _loadIP() async {
    final prefs = await SharedPreferences.getInstance();
    var pcs = await PcStore.load();
    final current = pcs.where((p) => p.id == PcStore.currentId).firstOrNull;
    if (current != null && (current.name == 'PC' || current.name == current.ip)) {
      await fetchPcName(current);
      pcs = await PcStore.load();
    }
    if (!mounted) return;
    setState(() {
      _pcs = pcs;
      _ipController.text = prefs.getString(Constants.ipAddrKey) ?? '';
    });
  }

  Widget _buildPcList() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(tr('Paired PCs'), style: Theme.of(context).textTheme.titleSmall),
        const SizedBox(height: 4),
        for (final pc in _pcs)
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: Icon(pc.id == PcStore.currentId
                ? Icons.radio_button_checked
                : Icons.radio_button_unchecked),
            title: Text(pc.name),
            subtitle: Text('${pc.ip}:${pc.port}'),
            onTap: pc.id == PcStore.currentId ? null : () => switchToPc(context, pc),
            trailing: PopupMenuButton<String>(
              onSelected: (v) => v == 'rename' ? _renamePc(pc) : _removePc(pc),
              itemBuilder: (_) => [
                PopupMenuItem(value: 'rename', child: Text(tr('Rename'))),
                PopupMenuItem(value: 'remove', child: Text(tr('Forget this PC'))),
              ],
            ),
          ),
        Text(
          tr('To add another PC, pair with its QR code (or type the IP below).'),
          style: TextStyle(
              fontSize: 12, color: Theme.of(context).colorScheme.onSurfaceVariant),
        ),
      ],
    );
  }

  Future<void> _renamePc(SavedPc pc) async {
    final ctrl = TextEditingController(text: pc.name);
    final name = await showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(tr('PC name')),
        content: TextField(controller: ctrl, autofocus: true),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx), child: Text(tr('Cancel'))),
          FilledButton(
              onPressed: () => Navigator.pop(ctx, ctrl.text.trim()),
              child: Text(tr('Save'))),
        ],
      ),
    );
    if (name == null || name.isEmpty) return;
    await PcStore.rename(pc.id, name);
    _loadIP();
  }

  Future<void> _removePc(SavedPc pc) async {
    final wasCurrent = pc.id == PcStore.currentId;
    await PcStore.remove(pc.id);
    final next = await PcStore.current();
    if (!mounted) return;
    if (wasCurrent && next != null) {
      await switchToPc(context, next);
    } else {
      _loadIP();
    }
  }

  Future<void> _findServer() async {
    setState(() => _isLoading = true);
    try {
      final discovery = ServerDiscoveryService();
      final result = await discovery.findServers('255.255.255.255', 5);
      if (result != 'Not Found') {
        _ipController.text = result;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(tr('Server found: {server}', {'server': result}))),
        );
      } else {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(tr('Server not found'))),
        );
      }
    } catch (e) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(friendlyError(e))),
      );
    } finally {
      setState(() => _isLoading = false);
    }
  }

  Future<void> _saveAndGoBack() async {
    var ip = _ipController.text.trim();
    if (ip.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(tr('Please enter a valid IP'))),
      );
      return;
    }

    ip = ip.replaceAll('http://', '').replaceAll('https://', '');
    if (ip.endsWith('/')) ip = ip.substring(0, ip.length - 1);

    String baseUrl;
    String port;
    if (ip.contains(':')) {
      baseUrl = 'http://$ip';
      port = ip.substring(ip.indexOf(':') + 1);
    } else {
      port = Constants.defaultPort;
      baseUrl = 'http://$ip:$port';
    }

    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(Constants.ipAddrKey, ip);
    await prefs.setString(Constants.portKey, port);
    final apiKey = prefs.getString(Constants.apiKeyKey);
    final host = ip.contains(':') ? ip.substring(0, ip.indexOf(':')) : ip;
    final pc = await PcStore.upsert(ip: host, port: port, apiKey: apiKey);
    fetchPcName(pc);

    final client = Injector.get<DroidDeckClient>();
    client.setBaseUrl(baseUrl);
    client.setApiKey(apiKey);

    final signalR = Injector.get<SignalRService>();
    await signalR.updateUrl(baseUrl, apiKey: apiKey);

    if (mounted) {
      Navigator.of(context).pushReplacementNamed('/home');
    }
  }

  Future<void> _pairWithQr() async {
    final result = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => const QrScanPage()),
    );
    if (result == null || !mounted) return;

    final uri = Uri.tryParse(result);
    final ip = uri?.queryParameters['ip'];
    final port = uri?.queryParameters['port'] ?? Constants.defaultPort;
    final key = uri?.queryParameters['key'];

    if (uri == null ||
        uri.scheme != 'droiddeck' ||
        ip == null ||
        ip.isEmpty ||
        key == null ||
        key.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(tr('That QR isn\'t a KZ Companion pairing code. Use the one in the PC\'s "Pair device (QR)" window.'))),
      );
      return;
    }

    final baseUrl = 'http://$ip:$port';
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(Constants.ipAddrKey, ip);
    await prefs.setString(Constants.portKey, port);
    await prefs.setString(Constants.apiKeyKey, key);
    final pc = await PcStore.upsert(ip: ip, port: port, apiKey: key);
    await fetchPcName(pc);

    final client = Injector.get<DroidDeckClient>();
    client.setBaseUrl(baseUrl);
    client.setApiKey(key);
    final signalR = Injector.get<SignalRService>();
    await signalR.updateUrl(baseUrl, apiKey: key);

    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(tr('Paired successfully!'))),
      );
      Navigator.of(context).pushReplacementNamed('/home');
    }
  }

  Widget _buildBrowserPairing() {
    return ListTile(
      contentPadding: EdgeInsets.zero,
      leading: const Icon(Icons.computer),
      title: FutureBuilder<dynamic>(
        future: Injector.get<DroidDeckClient>().getJson('/api/system/info'),
        builder: (context, snap) {
          final name = (snap.data is Map) ? (snap.data as Map)['name'] as String? : null;
          return Text(name ?? Uri.base.host);
        },
      ),
      subtitle: Text(tr('This browser is paired with the PC.')),
      trailing: TextButton(
        onPressed: () async {
          await (await SharedPreferences.getInstance()).remove(Constants.apiKeyKey);
          if (!mounted) return;
          Navigator.of(context).pushNamedAndRemoveUntil('/', (_) => false);
        },
        child: Text(tr('Forget')),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(tr('Settings')),
      ),
      body: Padding(
        padding: const EdgeInsets.all(16.0),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
            if (kIsWeb) _buildBrowserPairing(),
            if (!kIsWeb) ...[
            if (_pcs.isNotEmpty) ...[
              _buildPcList(),
              const Divider(height: 24),
            ],
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute(builder: (_) => const WelcomePage()),
                ),
                icon: const Icon(Icons.help_outline),
                label: Text(tr('Not installed on the PC yet? See how')),
              ),
            ),
            TextField(
              controller: _ipController,
              decoration: InputDecoration(
                labelText: tr('Server IP'),
                hintText: '192.168.x.x',
              ),
            ),
            const SizedBox(height: 16),
            _isLoading
                ? const CircularProgressIndicator()
                : ElevatedButton.icon(
                    onPressed: _findServer,
                    icon: const Icon(Icons.search),
                    label: Text(tr('Search for server')),
                  ),
            const SizedBox(height: 16),
            ElevatedButton.icon(
              onPressed: _pairWithQr,
              icon: const Icon(Icons.qr_code_scanner),
              label: Text(tr('Pair (QR)')),
            ),
            const SizedBox(height: 16),
            ElevatedButton(
              onPressed: _saveAndGoBack,
              child: Text(tr('Save')),
            ),
            ],
          ],
          ),
        ),
      ),
    );
  }

  @override
  void dispose() {
    _ipController.dispose();
    super.dispose();
  }
}
