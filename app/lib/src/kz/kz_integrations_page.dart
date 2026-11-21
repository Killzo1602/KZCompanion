import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

/// Settings → Integrations. Hoje só o Discord; outras integrações entram aqui depois.
class KzIntegrationsPage extends StatelessWidget {
  const KzIntegrationsPage({super.key, this.signalR});

  /// Só para testes: sem isso usa o do Injector.
  final SignalRService? signalR;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: const Text('Integrations'), toolbarHeight: 44),
      body: SafeArea(
        top: false,
        child: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 640),
            child: ListView(padding: const EdgeInsets.all(12), children: [
              KzCard(
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
                child: ListTile(
                  leading: const Icon(Icons.headset_mic_rounded),
                  title: const Text('Discord'),
                  subtitle: const Text('Connect KZ Companion to the Discord app on this PC'),
                  trailing: Row(mainAxisSize: MainAxisSize.min, children: [
                    Watch((context) {
                      final ds = (signalR ?? Injector.get<SignalRService>()).discordState.value;
                      if (ds['connected'] != true) return const SizedBox.shrink();
                      return const Text('Connected',
                          style: TextStyle(fontSize: 12, color: Color(0xFF4ADE80)));
                    }),
                    Icon(Icons.chevron_right_rounded, color: scheme.onSurfaceVariant),
                  ]),
                  onTap: () => Navigator.of(context)
                      .push(MaterialPageRoute(builder: (_) => const KzDiscordPage())),
                ),
              ),
            ]),
          ),
        ),
      ),
    );
  }
}

/// Credenciais do Discord: cada usuário cria o PRÓPRIO app no Discord Developer Portal (o RPC
/// só funciona para o dono do app, sem aprovação) e cola aqui o Client ID e o Secret.
class KzDiscordPage extends StatefulWidget {
  const KzDiscordPage({super.key});

  @override
  State<KzDiscordPage> createState() => _KzDiscordPageState();
}

class _KzDiscordPageState extends State<KzDiscordPage> {
  static const redirectUri = 'http://localhost:4787/discord';

  final _idCtrl = TextEditingController();
  final _secretCtrl = TextEditingController();
  bool _busy = false;
  bool _obscure = true;

  SignalRService get _signalR => Injector.get<SignalRService>();

  /// O PC já guarda o Client ID e o Secret: reconectar não pede nada de novo.
  bool get _secretSaved => _signalR.discordState.value['configured'] == true;

  @override
  void initState() {
    super.initState();
    Injector.get<DroidDeckClient>().getDiscordState().then((s) {
      if (s == null || !mounted) return;
      _signalR.discordState.value = Map<String, dynamic>.from(s);
      // O Client ID vem do PC; o Secret fica lá e nunca é mostrado.
      final savedId = s['clientId'];
      if (savedId is String && _idCtrl.text.isEmpty) _idCtrl.text = savedId;
      setState(() {});
    }).catchError((_) {});
  }

  @override
  void dispose() {
    _idCtrl.dispose();
    _secretCtrl.dispose();
    super.dispose();
  }

  void _snack(String msg) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
  }

  Future<void> _save() async {
    final id = _idCtrl.text.trim();
    final secret = _secretCtrl.text.trim();
    if (id.isEmpty || secret.isEmpty) {
      _snack('Enter the Client ID and the Client Secret.');
      return;
    }
    setState(() => _busy = true);
    try {
      await Injector.get<DroidDeckClient>().setDiscordConfig(id, secret);
      _snack('Credentials saved. Now tap Connect.');
    } catch (_) {
      _snack('Could not save. Is the PC reachable?');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _connect() async {
    setState(() => _busy = true);
    try {
      final state = await Injector.get<DroidDeckClient>().connectDiscord();
      _signalR.discordState.value = Map<String, dynamic>.from(state);
      _snack('Discord connected.');
    } catch (e) {
      _snack(e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final muted = Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6);
    return Scaffold(
      appBar: AppBar(title: const Text('Discord'), toolbarHeight: 44),
      body: SafeArea(
        top: false,
        child: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 640),
            child: ListView(padding: const EdgeInsets.all(12), children: [
              _status(),
              const SizedBox(height: 12),
              KzCard(
                title: 'Credentials',
                icon: Icons.key_rounded,
                child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  TextField(
                    controller: _idCtrl,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(labelText: 'Client ID', border: OutlineInputBorder()),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _secretCtrl,
                    obscureText: _obscure,
                    decoration: InputDecoration(
                      labelText: 'Client Secret',
                      hintText: _secretSaved ? 'Saved on the PC (fill in only to replace)' : null,
                      floatingLabelBehavior: _secretSaved ? FloatingLabelBehavior.always : null,
                      border: const OutlineInputBorder(),
                      suffixIcon: IconButton(
                        icon: Icon(_obscure ? Icons.visibility : Icons.visibility_off),
                        onPressed: () => setState(() => _obscure = !_obscure),
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  Row(children: [
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: _busy ? null : _save,
                        icon: const Icon(Icons.save_outlined, size: 18),
                        label: const Text('Save credentials'),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: FilledButton.icon(
                        onPressed: _busy ? null : _connect,
                        icon: _busy
                            ? const SizedBox(
                                width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
                            : const Icon(Icons.link_rounded, size: 18),
                        label: const Text('Connect'),
                      ),
                    ),
                  ]),
                  const SizedBox(height: 8),
                  Text('The first time, approve the prompt that opens in Discord on the PC.',
                      style: TextStyle(fontSize: 12, color: muted)),
                ]),
              ),
              const SizedBox(height: 12),
              KzCard(
                title: 'How to get the Client ID and Secret',
                icon: Icons.help_outline_rounded,
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  _step('1', 'Open discord.com/developers/applications and click New Application.'),
                  _step('2', 'Under OAuth2, copy the Client ID and the Client Secret (Reset Secret if needed).'),
                  _step('3', 'Still under OAuth2 → Redirects, add this address and save:'),
                  const SizedBox(height: 6),
                  InkWell(
                    onTap: () {
                      Clipboard.setData(const ClipboardData(text: redirectUri));
                      _snack('Redirect address copied.');
                    },
                    child: Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                        color: Colors.black26,
                        borderRadius: BorderRadius.circular(6),
                      ),
                      child: const Row(children: [
                        Expanded(
                            child: SelectableText(redirectUri, style: TextStyle(fontFamily: 'monospace'))),
                        Icon(Icons.copy, size: 18),
                      ]),
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Everyone uses their own Discord app: Discord only allows the app owner to use RPC, '
                    'with no approval needed. Never share your Secret.',
                    style: TextStyle(fontSize: 12, color: muted),
                  ),
                ]),
              ),
            ]),
          ),
        ),
      ),
    );
  }

  Widget _step(String n, String t) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 3),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('$n. ', style: const TextStyle(fontWeight: FontWeight.bold)),
          Expanded(child: Text(t)),
        ]),
      );

  Widget _status() {
    return Watch((context) {
      final ds = _signalR.discordState.value;
      final configured = ds['configured'] == true;
      final connected = ds['connected'] == true;
      final channel = ds['channelName'];
      final (Color color, IconData icon, String text) = !configured
          ? (const Color(0xFFFBBF24), Icons.warning_amber_rounded, 'Not set up. Enter the Client ID and Secret below.')
          : !connected
              ? (
                  const Color(0xFF90A4AE),
                  Icons.link_off_rounded,
                  'Set up, but not connected. With Discord open on the PC, tap Connect (no need to re-enter the fields).'
                )
              : (
                  const Color(0xFF4ADE80),
                  Icons.check_circle_rounded,
                  channel != null && '$channel'.isNotEmpty ? 'Connected, in channel "$channel".' : 'Connected.'
                );
      return Container(
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: color.withValues(alpha: 0.12),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: color.withValues(alpha: 0.4)),
        ),
        child: Row(children: [
          Icon(icon, color: color),
          const SizedBox(width: 10),
          Expanded(child: Text(text)),
        ]),
      );
    });
  }
}
