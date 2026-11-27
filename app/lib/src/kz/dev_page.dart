import 'dart:async';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_appearance.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_ring.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';

class DevPage extends StatefulWidget {
  const DevPage({super.key, required this.c});
  final KzController c;

  @override
  State<DevPage> createState() => _DevPageState();
}

class _DevPageState extends State<DevPage> with WidgetsBindingObserver {
  Map<String, dynamic>? _github;
  Map<String, dynamic>? _docker;
  String? _githubError;
  String? _dockerError;
  final _busy = <String>{};
  Timer? _githubTimer;
  Timer? _dockerTimer;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _start();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _stop();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _start();
    } else if (state == AppLifecycleState.paused || state == AppLifecycleState.hidden) {
      _stop();
    }
  }

  void _start() {
    _stop();
    _githubTimer = Timer.periodic(const Duration(seconds: 60), (_) => _loadGitHub());
    _dockerTimer = Timer.periodic(const Duration(seconds: 5), (_) => _loadDocker());
    _loadGitHub();
    _loadDocker();
  }

  void _stop() {
    _githubTimer?.cancel();
    _dockerTimer?.cancel();
  }

  String _explain(Object e) => e is DioException && e.response?.statusCode == 404
      ? 'Update KZ Companion on your PC.'
      : friendlyError(e);

  Future<void> _loadGitHub() async {
    try {
      final data = await widget.c.client.getGitHub();
      if (!mounted) return;
      setState(() {
        _github = data;
        _githubError = null;
      });
    } catch (e) {
      if (mounted) setState(() => _githubError = _explain(e));
    }
  }

  Future<void> _loadDocker() async {
    try {
      final data = await widget.c.client.getDocker();
      if (!mounted) return;
      setState(() {
        _docker = data;
        _dockerError = null;
      });
    } catch (e) {
      if (mounted) setState(() => _dockerError = _explain(e));
    }
  }

  Future<void> _command(Map container, String operation) async {
    final id = '${container['id']}';
    if (operation != 'start') {
      final verb = operation == 'stop' ? 'Stop' : 'Restart';
      final ok = await showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: Text('$verb ${container['name']}?'),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
            FilledButton(onPressed: () => Navigator.pop(ctx, true), child: Text(verb)),
          ],
        ),
      );
      if (ok != true || !mounted) return;
    }
    setState(() => _busy.add(id));
    try {
      await widget.c.client.dockerCommand(id, operation);
    } catch (e) {
      if (mounted) kzToast(context, _explain(e));
    }
    await _loadDocker();
    if (mounted) setState(() => _busy.remove(id));
  }

  @override
  Widget build(BuildContext context) {
    final gap = KzPrefs.density.value.gap;
    return Padding(
      padding: EdgeInsets.all(gap),
      child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Expanded(child: _githubCard()),
        SizedBox(width: gap),
        Expanded(child: _dockerCard()),
      ]),
    );
  }

  Color get _muted => Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.55);

  Widget _note(String text) => Center(
        child: Text(text, textAlign: TextAlign.center, style: TextStyle(color: _muted)),
      );

  Widget _card(String title, IconData icon, Color color, Widget child) =>
      KzCard(expand: true, title: title, icon: icon, accent: color, child: SizedBox.expand(child: child));

  Widget _githubCard() {
    final data = _github;
    final problem = _githubError ?? data?['error'] as String?;
    Widget body;
    if (data != null && data['installed'] == false) {
      body = _note('GitHub CLI (gh) is not installed on the PC.');
    } else if (problem != null) {
      body = _note(problem);
    } else if (data == null) {
      body = const Center(child: CircularProgressIndicator());
    } else {
      final prs = ((data['pullRequests'] as List?) ?? const []).cast<Map>();
      final repos = ((data['repos'] as List?) ?? const []).cast<Map>();
      body = ListView(padding: EdgeInsets.zero, children: [
        if (prs.isNotEmpty) _section('Open pull requests'),
        for (final p in prs) _check(p, '${p['repo']} ${p['branch']}'),
        if (repos.isNotEmpty) _section('Latest pushes'),
        for (final r in repos) _check(r, '${r['repo']}  ·  ${r['branch']}'),
      ]);
    }
    return _card('GitHub', Icons.merge_type_rounded, KzColors.codex, body);
  }

  Widget _section(String text) => Padding(
        padding: const EdgeInsets.only(top: 4, bottom: 2),
        child: Text(text, style: TextStyle(fontSize: 11, fontWeight: FontWeight.w700, color: _muted)),
      );

  Widget _check(Map item, String heading) {
    final (icon, color, label) = switch ('${item['state']}') {
      'success' => (Icons.check_circle_rounded, const Color(0xFF4ADE80), 'CI passed'),
      'failure' => (Icons.cancel_rounded, KzColors.danger, 'CI failed'),
      'pending' => (Icons.timelapse_rounded, const Color(0xFFFBBF24), 'CI running'),
      _ => (Icons.remove_circle_outline_rounded, _muted, 'No CI'),
    };
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(children: [
        Icon(icon, size: 20, color: color, semanticLabel: label),
        const SizedBox(width: 8),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(heading,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700)),
            Text('${item['title']}',
                maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 11.5, color: _muted)),
          ]),
        ),
      ]),
    );
  }

  Widget _dockerCard() {
    final data = _docker;
    final problem = _dockerError ?? data?['error'] as String?;
    final list = ((data?['containers'] as List?) ?? const []).cast<Map>();
    Widget body;
    if (data != null && data['installed'] == false) {
      body = _note('Docker is not installed on the PC.');
    } else if (problem != null) {
      body = _note(problem);
    } else if (data == null) {
      body = const Center(child: CircularProgressIndicator());
    } else if (list.isEmpty) {
      body = _note('No containers.');
    } else {
      body = ListView(padding: EdgeInsets.zero, children: [for (final c in list) _container(c)]);
    }
    return _card('Docker', Icons.view_in_ar_rounded, KzColors.audio, body);
  }

  Widget _container(Map item) {
    final id = '${item['id']}';
    final running = item['running'] == true;
    return Row(children: [
      Container(
        width: 9,
        height: 9,
        decoration: BoxDecoration(color: running ? const Color(0xFF4ADE80) : _muted, shape: BoxShape.circle),
      ),
      const SizedBox(width: 10),
      Expanded(
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text('${item['name']}',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700)),
          Text('${item['status']}',
              maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 11.5, color: _muted)),
        ]),
      ),
      if (_busy.contains(id))
        const Padding(
          padding: EdgeInsets.all(12),
          child: SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2)),
        )
      else if (running) ...[
        IconButton(
            tooltip: 'Restart', onPressed: () => _command(item, 'restart'), icon: const Icon(Icons.restart_alt_rounded)),
        IconButton(
            tooltip: 'Stop',
            onPressed: () => _command(item, 'stop'),
            icon: const Icon(Icons.stop_rounded, color: KzColors.danger)),
      ] else
        IconButton(
            tooltip: 'Start',
            onPressed: () => _command(item, 'start'),
            icon: const Icon(Icons.play_arrow_rounded, color: Color(0xFF4ADE80))),
    ]);
  }
}
