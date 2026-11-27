import 'dart:async';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

class KzSpotifySetup extends StatefulWidget {
  const KzSpotifySetup({super.key, required this.client});
  final DroidDeckClient client;

  @override
  State<KzSpotifySetup> createState() => _KzSpotifySetupState();
}

class _KzSpotifySetupState extends State<KzSpotifySetup> {
  final _idCtrl = TextEditingController();
  Map<String, dynamic> _status = const {};
  bool _busy = false;
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _poll?.cancel();
    _idCtrl.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final s = await widget.client.getIntegration('spotify');
      if (!mounted) return;
      setState(() {
        _status = s;
        final fields = (s['fields'] as Map?) ?? const {};
        final saved = fields['clientId'];
        if (saved is String && saved.isNotEmpty && _idCtrl.text.isEmpty) _idCtrl.text = saved;
      });
      if (s['loginPending'] == true) _startPolling();
    } catch (_) {}
  }

  void _startPolling() {
    _poll?.cancel();
    var n = 0;
    _poll = Timer.periodic(const Duration(seconds: 2), (t) async {
      n++;
      try {
        final s = await widget.client.getIntegration('spotify');
        if (!mounted) return t.cancel();
        setState(() => _status = s);
        if (s['loginPending'] != true || n > 60) t.cancel();
      } catch (_) {}
    });
  }

  Future<void> _login() async {
    setState(() => _busy = true);
    try {
      final s = await widget.client.configureIntegration('spotify', {'clientId': _idCtrl.text.trim()});
      if (!mounted) return;
      setState(() => _status = s);
      if (s['message'] == null) _startPolling();
    } catch (e) {
      _toast('Could not start the login: ${friendlyError(e)}');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _logout() async {
    setState(() => _busy = true);
    try {
      final s = await widget.client.logoutIntegration('spotify');
      if (mounted) setState(() => _status = s);
    } catch (e) {
      _toast(friendlyError(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _toast(String m) {
    if (mounted) kzToast(context, m);
  }

  @override
  Widget build(BuildContext context) {
    final muted = Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6);
    final connected = _status['connected'] == true;
    final pending = _status['loginPending'] == true;
    final redirect = '${_status['redirectUri'] ?? 'http://127.0.0.1:4787/api/integrations/spotify/callback'}';
    return KzCard(
      title: 'Spotify account',
      icon: Icons.music_note_rounded,
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        if (connected)
          Text('Signed in as ${_status['account'] ?? 'your account'}.', style: const TextStyle(fontSize: 13))
        else ...[
          TextField(
            controller: _idCtrl,
            decoration: const InputDecoration(labelText: 'Client ID', border: OutlineInputBorder()),
          ),
          const SizedBox(height: 10),
          InkWell(
            onTap: () {
              Clipboard.setData(ClipboardData(text: redirect));
              _toast('Redirect address copied.');
            },
            child: Container(
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(color: Colors.black26, borderRadius: BorderRadius.circular(6)),
              child: Row(children: [
                Expanded(child: SelectableText(redirect, style: const TextStyle(fontFamily: 'monospace', fontSize: 12))),
                const Icon(Icons.copy, size: 16),
              ]),
            ),
          ),
          const SizedBox(height: 6),
          Text('Add this redirect address to your Spotify app before logging in.',
              style: TextStyle(fontSize: 12, color: muted)),
        ],
        if (_status['message'] != null) ...[
          const SizedBox(height: 8),
          Text('${_status['message']}', style: const TextStyle(color: Color(0xFFFBBF24), fontSize: 13)),
        ],
        if (pending) ...[
          const SizedBox(height: 8),
          Text('Finish the login in the browser window that opened on this PC.',
              style: TextStyle(fontSize: 13, color: muted)),
        ],
        const SizedBox(height: 10),
        if (connected)
          OutlinedButton.icon(
            onPressed: _busy ? null : _logout,
            icon: const Icon(Icons.link_off_rounded, size: 18),
            label: const Text('Disconnect'),
          )
        else
          FilledButton.icon(
            onPressed: _busy ? null : _login,
            icon: const Icon(Icons.login_rounded, size: 18),
            label: const Text('Log in with Spotify'),
          ),
      ]),
    );
  }
}

class KzObsSetup extends StatefulWidget {
  const KzObsSetup({super.key, required this.client});
  final DroidDeckClient client;

  @override
  State<KzObsSetup> createState() => _KzObsSetupState();
}

class _KzObsSetupState extends State<KzObsSetup> {
  final _host = TextEditingController(text: 'localhost');
  final _port = TextEditingController(text: '4455');
  final _password = TextEditingController();
  bool _busy = false;
  bool _obscure = true;
  bool? _connected;
  String? _message;

  @override
  void initState() {
    super.initState();
    widget.client.getObsState().then((s) {
      if (mounted) setState(() => _connected = s?['connected'] == true);
    });
  }

  @override
  void dispose() {
    _host.dispose();
    _port.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _connect() async {
    final port = int.tryParse(_port.text.trim());
    if (port == null || port < 1 || port > 65535) {
      setState(() => _message = 'Enter a valid port (OBS uses 4455 by default).');
      return;
    }
    setState(() {
      _busy = true;
      _message = null;
    });
    try {
      await widget.client.setObsConfig(
          host: _host.text.trim().isEmpty ? 'localhost' : _host.text.trim(), port: port, password: _password.text);
      final s = await widget.client.connectObs();
      if (mounted) setState(() => _connected = s['connected'] == true);
    } on DioException catch (e) {
      final err = (e.response?.data is Map) ? (e.response!.data as Map)['error'] : null;
      if (mounted) setState(() => _message = '${err ?? friendlyError(e)}');
    } catch (e) {
      if (mounted) setState(() => _message = friendlyError(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final muted = Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6);
    return KzCard(
      title: 'OBS WebSocket',
      icon: Icons.videocam_rounded,
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        if (_connected == true)
          const Text('Connected to OBS.', style: TextStyle(fontSize: 13, color: Color(0xFF4ADE80))),
        Row(children: [
          Expanded(
            flex: 3,
            child: TextField(controller: _host, decoration: const InputDecoration(labelText: 'Address', border: OutlineInputBorder())),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: TextField(
              controller: _port,
              keyboardType: TextInputType.number,
              decoration: const InputDecoration(labelText: 'Port', border: OutlineInputBorder()),
            ),
          ),
        ]),
        const SizedBox(height: 10),
        TextField(
          controller: _password,
          obscureText: _obscure,
          decoration: InputDecoration(
            labelText: 'Password',
            helperText: 'Leave empty only if OBS has no password. It stays on this PC.',
            border: const OutlineInputBorder(),
            suffixIcon: IconButton(
              icon: Icon(_obscure ? Icons.visibility : Icons.visibility_off),
              onPressed: () => setState(() => _obscure = !_obscure),
            ),
          ),
        ),
        if (_message != null) ...[
          const SizedBox(height: 8),
          Text(_message!, style: const TextStyle(color: Color(0xFFFBBF24), fontSize: 13)),
        ],
        const SizedBox(height: 10),
        FilledButton.icon(
          onPressed: _busy ? null : _connect,
          icon: _busy
              ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
              : const Icon(Icons.link_rounded, size: 18),
          label: const Text('Save and connect'),
        ),
        const SizedBox(height: 6),
        Text('Saved settings are not shown again. Saving replaces the stored address, port and password.',
            style: TextStyle(fontSize: 12, color: muted)),
      ]),
    );
  }
}
