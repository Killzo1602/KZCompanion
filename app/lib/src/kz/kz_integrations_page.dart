import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_integration_catalog.dart';
import 'package:droiddeck/src/kz/kz_integration_setup.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

class KzLive {
  const KzLive({this.connected = false, this.configured = false, this.account, this.note});
  final bool connected;
  final bool configured;
  final String? account;

  final String? note;
}

const _green = Color(0xFF4ADE80);
const _amber = Color(0xFFFBBF24);
const _grey = Color(0xFF90A4AE);

class KzIntegrationsPage extends StatefulWidget {
  const KzIntegrationsPage({super.key, this.signalR, this.client, this.canConfigure});

  final SignalRService? signalR;
  final DroidDeckClient? client;

  final bool? canConfigure;

  @override
  State<KzIntegrationsPage> createState() => _KzIntegrationsPageState();
}

class _KzIntegrationsPageState extends State<KzIntegrationsPage> {
  Map<String, KzLive> _live = {};
  bool _loaded = false;

  bool get _canConfigure => widget.canConfigure ?? WebMode.configurator;
  SignalRService get _signalR => widget.signalR ?? Injector.get<SignalRService>();
  DroidDeckClient get _client => widget.client ?? Injector.get<DroidDeckClient>();

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  Future<void> _refresh() async {
    final live = <String, KzLive>{};
    try {
      for (final i in await _client.getIntegrations()) {
        live['${i['name']}'] = KzLive(
          connected: i['connected'] == true,
          configured: i['connected'] == true,
          account: i['account'] as String?,
        );
      }
    } catch (_) {}
    try {
      final d = await _client.getDiscordState();
      if (d != null) {
        _signalR.discordState.value = Map<String, dynamic>.from(d);
        live['discord'] = KzLive(
          connected: d['connected'] == true,
          configured: d['configured'] == true,
          account: d['channelName'] as String?,
        );
      }
    } catch (_) {}
    try {
      final spotify = await _client.getIntegration('spotify');
      live['spotify'] = KzLive(
        connected: spotify['connected'] == true,
        configured: spotify['configured'] == true,
        account: spotify['account'] as String?,
      );
    } catch (_) {}
    try {
      final obs = await _client.getObsState();
      if (obs != null) {
        live['obs'] = KzLive(connected: obs['connected'] == true, configured: obs['connected'] == true);
      }
    } catch (_) {}
    try {
      final sessions = await _client.getMediaSessions();
      final apple = sessions.where((s) => (s.id ?? '').toLowerCase().contains('apple')).isNotEmpty;
      live['apple_music'] = KzLive(
        note: apple
            ? 'An Apple Music media session is present. Playback controls have not been tested.'
            : 'No Apple Music media session found right now.',
      );
    } catch (_) {}
    if (!mounted) return;
    setState(() {
      _live = live;
      _loaded = true;
    });
  }

  Future<void> _open(KzIntegration info) async {
    await Navigator.of(context).push(MaterialPageRoute(
      builder: (_) => KzIntegrationDetailPage(
        info: info,
        live: _live[info.id] ?? _live[info.backendName ?? ''],
        canConfigure: _canConfigure,
        client: _client,
        signalR: _signalR,
      ),
    ));
    if (mounted) _refresh();
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final muted = scheme.onSurface.withValues(alpha: 0.6);
    return Scaffold(
      appBar: AppBar(title: const Text('Integrations'), toolbarHeight: 44),
      body: SafeArea(
        top: false,
        child: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 760),
            child: RefreshIndicator(
              onRefresh: _refresh,
              child: ListView(padding: const EdgeInsets.all(12), children: [
                if (!_canConfigure)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Icon(Icons.computer_rounded, size: 16, color: muted),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          'Passwords, secrets and logins are entered on your PC, never on this device. '
                          'Open KZ Companion on Windows, then Integrations, to set one up. This list shows what the PC reports.',
                          style: TextStyle(fontSize: 12.5, color: muted),
                        ),
                      ),
                    ]),
                  ),
                for (final i in KzCatalog.priority) _card(i),
                const Padding(
                  padding: EdgeInsets.fromLTRB(4, 14, 0, 8),
                  child: Text('MORE IN THE CODE, NOT AVAILABLE YET',
                      style: TextStyle(fontSize: 11, letterSpacing: 1.1, fontWeight: FontWeight.w600)),
                ),
                for (final i in KzCatalog.more) _card(i, compact: true),
                Padding(
                  padding: const EdgeInsets.fromLTRB(4, 12, 0, 4),
                  child: Text(
                    'Nothing is marked connected unless the PC reports it. '
                    'Integrations marked "Setup available" have been reviewed against current documentation but not tested with a live account yet.',
                    style: TextStyle(fontSize: 11.5, color: muted),
                  ),
                ),
              ]),
            ),
          ),
        ),
      ),
    );
  }

  Widget _card(KzIntegration i, {bool compact = false}) {
    final live = _live[i.id] ?? _live[i.backendName ?? ''];
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: KzCard(
        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
        child: ListTile(
          key: ValueKey('integration-${i.id}'),
          leading: Icon(i.icon),
          title: Text(i.name),
          subtitle: Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(i.summary),
              if (!compact) ...[
                const SizedBox(height: 6),
                Wrap(spacing: 6, runSpacing: 4, children: [for (final c in i.capabilities.take(3)) _Chip(c)]),
              ],
              const SizedBox(height: 6),
              Wrap(spacing: 8, runSpacing: 4, crossAxisAlignment: WrapCrossAlignment.center, children: [
                _SupportBadge(i.support),
                if (i.support == KzSupport.setupAvailable) _StatusBadge(live: live, loaded: _loaded),
              ]),
            ]),
          ),
          isThreeLine: true,
          trailing: const Icon(Icons.chevron_right_rounded),
          onTap: () => _open(i),
        ),
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip(this.text);
  final String text;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHighest.withValues(alpha: 0.6),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(text, style: const TextStyle(fontSize: 11.5)),
    );
  }
}

class _SupportBadge extends StatelessWidget {
  const _SupportBadge(this.support);
  final KzSupport support;

  @override
  Widget build(BuildContext context) {
    final color = switch (support) {
      KzSupport.setupAvailable => Theme.of(context).colorScheme.primary,
      KzSupport.candidate => _amber,
      _ => _grey,
    };
    return Row(mainAxisSize: MainAxisSize.min, children: [
      Icon(support.icon, size: 14, color: color),
      const SizedBox(width: 4),
      Text(support.label, style: TextStyle(fontSize: 12, color: color, fontWeight: FontWeight.w600)),
    ]);
  }
}

class _StatusBadge extends StatelessWidget {
  const _StatusBadge({required this.live, required this.loaded});
  final KzLive? live;
  final bool loaded;

  @override
  Widget build(BuildContext context) {
    final (Color color, String text) = !loaded
        ? (_grey, 'Checking…')
        : live == null
            ? (_grey, 'Status unknown')
            : live!.connected
                ? (_green, live!.account != null && live!.account!.isNotEmpty ? 'Connected · ${live!.account}' : 'Connected')
                : live!.configured
                    ? (_grey, 'Not connected')
                    : (_amber, 'Not set up');
    return Row(mainAxisSize: MainAxisSize.min, children: [
      Icon(Icons.circle, size: 9, color: color),
      const SizedBox(width: 5),
      Text(text, style: TextStyle(fontSize: 12, color: color)),
    ]);
  }
}

class KzIntegrationDetailPage extends StatelessWidget {
  const KzIntegrationDetailPage({
    super.key,
    required this.info,
    required this.live,
    required this.canConfigure,
    required this.client,
    required this.signalR,
  });

  final KzIntegration info;
  final KzLive? live;
  final bool canConfigure;
  final DroidDeckClient client;
  final SignalRService signalR;

  @override
  Widget build(BuildContext context) {
    final muted = Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6);
    return Scaffold(
      appBar: AppBar(title: Text(info.name), toolbarHeight: 44),
      body: SafeArea(
        top: false,
        child: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 680),
            child: ListView(padding: const EdgeInsets.all(12), children: [
              KzCard(
                title: 'Status',
                icon: Icons.info_outline_rounded,
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Wrap(spacing: 12, runSpacing: 6, children: [
                    _SupportBadge(info.support),
                    if (info.support == KzSupport.setupAvailable) _StatusBadge(live: live, loaded: true),
                  ]),
                  if (live?.note != null) ...[
                    const SizedBox(height: 8),
                    Text(live!.note!, style: const TextStyle(fontSize: 13)),
                  ],
                  const SizedBox(height: 8),
                  Text(info.verification, style: TextStyle(fontSize: 12, color: muted)),
                ]),
              ),
              const SizedBox(height: 12),
              KzCard(
                title: 'What it can do',
                icon: Icons.bolt_rounded,
                child: _bullets(info.capabilities),
              ),
              const SizedBox(height: 12),
              KzCard(
                  title: 'Requirements',
                  icon: Icons.checklist_rounded,
                  accent: KzColors.audio,
                  child: _bullets(info.requirements)),
              if (info.caveats.isNotEmpty) ...[
                const SizedBox(height: 12),
                KzCard(
                    title: 'Limits',
                    icon: Icons.warning_amber_rounded,
                    accent: KzColors.apps,
                    child: _bullets(info.caveats)),
              ],
              const SizedBox(height: 12),
              _actions(context, muted),
            ]),
          ),
        ),
      ),
    );
  }

  Widget _bullets(List<String> items) => Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        for (final t in items)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 3),
            child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              const Text('•  '),
              Expanded(child: Text(t, style: const TextStyle(fontSize: 13))),
            ]),
          ),
      ]);

  Widget _actions(BuildContext context, Color muted) {
    if (info.support != KzSupport.setupAvailable) {
      return Text(
        info.support == KzSupport.candidate
            ? 'There is nothing to set up yet.'
            : 'There is no setup screen for this integration yet.',
        style: TextStyle(color: muted, fontSize: 13),
      );
    }
    if (!canConfigure) {
      return const KzCard(
        title: 'Set up',
        icon: Icons.computer_rounded,
        child: Text(
          'Set this up on your PC: open KZ Companion on Windows and go to Integrations. '
          'Credentials are never entered on the phone.',
          style: TextStyle(fontSize: 13),
        ),
      );
    }
    return switch (info.id) {
      'discord' => FilledButton.icon(
          onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const KzDiscordPage())),
          icon: const Icon(Icons.tune_rounded, size: 18),
          label: const Text('Set up Discord'),
        ),
      'spotify' => KzSpotifySetup(client: client),
      'obs' => KzObsSetup(client: client),
      _ => const SizedBox.shrink(),
    };
  }
}

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

  bool get _secretSaved => _signalR.discordState.value['configured'] == true;

  @override
  void initState() {
    super.initState();
    Injector.get<DroidDeckClient>().getDiscordState().then((s) {
      if (s == null || !mounted) return;
      _signalR.discordState.value = Map<String, dynamic>.from(s);
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
                      hintText: _secretSaved ? 'Saved on this PC (fill in only to replace)' : null,
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
                  Text('The first time, approve the prompt that opens in Discord on this PC.',
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
                    'Discord limits these voice permissions to approved apps. Its documentation lets an '
                    'unapproved app work for users on the app\'s tester list (up to 50), so add your own '
                    'Discord account as a tester of your app. This has not been confirmed on a live account '
                    'yet. Never share your Secret.',
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
          ? (_amber, Icons.warning_amber_rounded, 'Not set up. Enter the Client ID and Secret below.')
          : !connected
              ? (
                  _grey,
                  Icons.link_off_rounded,
                  'Set up, but not connected. With Discord open on the PC, tap Connect (no need to re-enter the fields).'
                )
              : (
                  _green,
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
