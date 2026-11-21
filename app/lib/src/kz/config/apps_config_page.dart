import 'dart:async';
import 'dart:convert';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/config/drop_import.dart';
import 'package:droiddeck/src/kz/kz_controller.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';

/// Biblioteca de aplicativos no configurador do PC: Add Application abre o seletor de
/// arquivos do Windows (no PC), puxa nome e ícone sozinho e salva. Nada de digitar caminho.
class AppsConfigPage extends StatefulWidget {
  const AppsConfigPage({super.key});

  @override
  State<AppsConfigPage> createState() => _AppsConfigPageState();
}

class _AppsConfigPageState extends State<AppsConfigPage> {
  final _client = Injector.get<DroidDeckClient>();
  List<KzApp> _apps = [];
  bool _loading = true;
  String? _error;
  bool _dragging = false;
  StreamSubscription<DroppedFiles>? _dropSub;
  StreamSubscription<bool>? _dragSub;

  @override
  void initState() {
    super.initState();
    _load();
    // Arrastar .exe/.lnk do Explorer para ca (so no WebView2 do app do PC).
    DropImport.start();
    _dropSub = DropImport.files.listen(_importDropped);
    _dragSub = DropImport.dragging.listen((v) {
      if (mounted && v != _dragging) setState(() => _dragging = v);
    });
  }

  @override
  void dispose() {
    _dropSub?.cancel();
    _dragSub?.cancel();
    super.dispose();
  }

  /// Cada arquivo solto: valida (.exe/.lnk) no servidor, pega nome e icone sozinho e salva pela
  /// mesma API do botao Add Application. Duplicata (mesmo caminho ou atalho do mesmo programa) e recusada.
  Future<void> _importDropped(DroppedFiles dropped) async {
    var added = 0;
    final problems = <String>[...dropped.rejected.map((n) => '$n: only .exe and .lnk files are supported')];
    for (final path in dropped.paths) {
      try {
        final preview = await _client.previewApp(path);
        await _client.saveApp(name: '${preview['name']}', path: '${preview['path']}');
        added++;
      } on DioException catch (e) {
        final msg = e.response?.data is Map ? '${(e.response!.data as Map)['error']}' : friendlyError(e);
        problems.add(msg);
      } catch (e) {
        problems.add(friendlyError(e));
      }
    }
    if (!mounted) return;
    await _load();
    if (!mounted) return;
    final parts = <String>[
      if (added > 0) 'Added $added application${added == 1 ? '' : 's'}',
      ...problems,
    ];
    if (parts.isNotEmpty) kzToast(context, parts.join(' · '));
  }

  /// Reordena os favoritos (arrastando) e grava a ordem; os demais ficam depois, como estavam.
  Future<void> _reorderFavorites(int oldIndex, int newIndex) async {
    final favs = _apps.where((a) => a.favorite).toList();
    if (newIndex > oldIndex) newIndex -= 1;
    final moved = favs.removeAt(oldIndex);
    favs.insert(newIndex, moved);
    final others = _apps.where((a) => !a.favorite).toList();
    setState(() => _apps = [...favs, ...others]);
    try {
      await _client.reorderApps([for (final a in _apps) a.id]);
    } catch (e) {
      if (mounted) kzToast(context, friendlyError(e));
      _load();
    }
  }

  Future<void> _load() async {
    try {
      final raw = await _client.getApps();
      if (!mounted) return;
      setState(() {
        _apps = raw.map(KzApp.fromJson).toList();
        _loading = false;
        _error = null;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = friendlyError(e);
      });
    }
  }

  Future<void> _addFromBrowse() async {
    try {
      final picked = await _client.pickApp();
      if (picked == null || !mounted) return;
      final dup = _apps.where((a) => (a.path ?? '').toLowerCase() == '${picked['path']}'.toLowerCase());
      if (dup.isNotEmpty) {
        kzToast(context, '"${dup.first.name}" is already in your library.');
        return;
      }
      await _edit(draft: picked);
    } catch (e) {
      if (mounted) kzToast(context, friendlyError(e));
    }
  }

  Future<void> _addFromInstalled() async {
    final chosen = await showDialog<Map<String, dynamic>>(
      context: context,
      builder: (_) => _InstalledDialog(client: _client),
    );
    if (chosen == null || !mounted) return;
    try {
      final preview = await _client.previewApp('${chosen['path']}');
      if (mounted) await _edit(draft: preview);
    } catch (e) {
      if (mounted) kzToast(context, friendlyError(e));
    }
  }

  /// [draft] = {path, name, iconBase64} de um arquivo recém-escolhido; [existing] = edição.
  Future<void> _edit({Map<String, dynamic>? draft, KzApp? existing}) async {
    final groups = {for (final a in _apps) if (a.group.isNotEmpty) a.group}.toList()..sort();
    final saved = await showDialog<bool>(
      context: context,
      builder: (_) => _AppEditDialog(client: _client, draft: draft, existing: existing, groups: groups),
    );
    if (saved == true) _load();
  }

  Future<void> _delete(KzApp a) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('Remove ${a.name}?'),
        content: const Text('Only removes it from KZ Companion. The program stays installed.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Remove')),
        ],
      ),
    );
    if (ok != true) return;
    try {
      await _client.deleteApp(a.id);
      _load();
    } catch (e) {
      if (mounted) kzToast(context, friendlyError(e));
    }
  }

  Future<void> _toggleFavorite(KzApp a) async {
    try {
      await _client.saveApp(id: a.id, name: a.name, group: a.group, favorite: !a.favorite);
      _load();
    } catch (e) {
      if (mounted) kzToast(context, friendlyError(e));
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Applications'),
        actions: [
          OutlinedButton.icon(
            onPressed: _addFromInstalled,
            icon: const Icon(Icons.list_alt_rounded, size: 18),
            label: const Text('Installed apps'),
          ),
          const SizedBox(width: 8),
          FilledButton.icon(
            onPressed: _addFromBrowse,
            icon: const Icon(Icons.add_rounded),
            label: const Text('Add Application'),
          ),
          const SizedBox(width: 16),
        ],
      ),
      body: Stack(children: [
        _content(scheme),
        if (_dragging)
          Positioned.fill(
            child: IgnorePointer(
              child: Container(
                margin: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: scheme.primary.withValues(alpha: 0.10),
                  borderRadius: BorderRadius.circular(18),
                  border: Border.all(color: scheme.primary, width: 2),
                ),
                child: Center(
                  child: Column(mainAxisSize: MainAxisSize.min, children: [
                    Icon(Icons.file_download_outlined, size: 48, color: scheme.primary),
                    const SizedBox(height: 8),
                    const Text('Drop .exe or .lnk files to add them',
                        style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600)),
                  ]),
                ),
              ),
            ),
          ),
      ]),
    );
  }

  Widget _content(ColorScheme scheme) {
    return Builder(builder: (context) {
      return _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? Center(child: Text(_error!))
              : _apps.isEmpty
                  ? Center(
                      child: Column(mainAxisSize: MainAxisSize.min, children: [
                        Icon(Icons.apps_rounded, size: 48, color: scheme.primary),
                        const SizedBox(height: 12),
                        const Text('Your application library is empty',
                            style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600)),
                        const SizedBox(height: 6),
                        const Text('Click Add Application and pick an .exe or shortcut. The icon is picked up automatically.'),
                      ]),
                    )
                  : ListView(padding: const EdgeInsets.all(20), children: [
                      if (_apps.any((a) => a.favorite)) ...[
                        _sectionTitle('Favorites', 'drag to reorder; the phone shows them in this order', scheme),
                        ConstrainedBox(
                          constraints: const BoxConstraints(maxWidth: 460),
                          child: ReorderableListView(
                            shrinkWrap: true,
                            physics: const NeverScrollableScrollPhysics(),
                            buildDefaultDragHandles: false,
                            onReorder: _reorderFavorites,
                            children: [
                              for (final (i, a) in _apps.where((a) => a.favorite).indexed)
                                Padding(
                                  key: ValueKey('fav${a.id}'),
                                  padding: const EdgeInsets.only(bottom: 8),
                                  child: _card(a, scheme, width: 460, handle: ReorderableDragStartListener(
                                    index: i,
                                    child: const MouseRegion(
                                      cursor: SystemMouseCursors.grab,
                                      child: Padding(
                                        padding: EdgeInsets.only(right: 8),
                                        child: Icon(Icons.drag_indicator_rounded),
                                      ),
                                    ),
                                  )),
                                ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 16),
                      ],
                      _sectionTitle('Applications', 'drop .exe or .lnk files anywhere in this window to add them', scheme),
                      Wrap(spacing: 14, runSpacing: 14, children: [
                        for (final a in _apps.where((a) => !a.favorite)) _card(a, scheme),
                      ]),
                    ]);
    });
  }

  Widget _sectionTitle(String title, String hint, ColorScheme scheme) => Padding(
        padding: const EdgeInsets.only(bottom: 10),
        child: Row(crossAxisAlignment: CrossAxisAlignment.baseline, textBaseline: TextBaseline.alphabetic, children: [
          Text(title.toUpperCase(),
              style: TextStyle(fontSize: 12, letterSpacing: 1.1, fontWeight: FontWeight.w700, color: scheme.primary)),
          const SizedBox(width: 10),
          Text(hint, style: TextStyle(fontSize: 11.5, color: scheme.onSurface.withValues(alpha: 0.5))),
        ]),
      );

  Widget _card(KzApp a, ColorScheme scheme, {double width = 270, Widget? handle}) {
    return Container(
      width: width,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLow,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.outlineVariant),
      ),
      child: Row(children: [
        if (handle != null) handle,
        KzAppIcon(a, size: 52),
        const SizedBox(width: 12),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(a.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontWeight: FontWeight.w600)),
            Text(a.group.isEmpty ? 'No group' : a.group,
                style: TextStyle(fontSize: 12, color: scheme.onSurface.withValues(alpha: 0.6))),
            Text(a.path ?? '',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 10, color: scheme.onSurface.withValues(alpha: 0.4))),
          ]),
        ),
        Column(mainAxisSize: MainAxisSize.min, children: [
          IconButton(
            visualDensity: VisualDensity.compact,
            tooltip: a.favorite ? 'Remove from favorites' : 'Add to favorites',
            icon: Icon(a.favorite ? Icons.star_rounded : Icons.star_border_rounded,
                color: a.favorite ? scheme.primary : null),
            onPressed: () => _toggleFavorite(a),
          ),
          Row(mainAxisSize: MainAxisSize.min, children: [
            IconButton(
                visualDensity: VisualDensity.compact,
                tooltip: 'Edit',
                icon: const Icon(Icons.edit_outlined, size: 18),
                onPressed: () => _edit(existing: a)),
            IconButton(
                visualDensity: VisualDensity.compact,
                tooltip: 'Remove',
                icon: const Icon(Icons.delete_outline_rounded, size: 18),
                onPressed: () => _delete(a)),
          ]),
        ]),
      ]),
    );
  }
}

class _InstalledDialog extends StatefulWidget {
  const _InstalledDialog({required this.client});
  final DroidDeckClient client;

  @override
  State<_InstalledDialog> createState() => _InstalledDialogState();
}

class _InstalledDialogState extends State<_InstalledDialog> {
  List<Map<String, dynamic>>? _all;
  String _q = '';

  @override
  void initState() {
    super.initState();
    widget.client.getInstalledApps().then((v) {
      if (mounted) setState(() => _all = v);
    }).catchError((_) {
      if (mounted) setState(() => _all = []);
    });
  }

  @override
  Widget build(BuildContext context) {
    final list = (_all ?? []).where((a) => '${a['name']}'.toLowerCase().contains(_q.toLowerCase())).toList();
    return AlertDialog(
      title: const Text('Installed applications'),
      content: SizedBox(
        width: 420,
        height: 420,
        child: Column(children: [
          TextField(
            autofocus: true,
            decoration: const InputDecoration(prefixIcon: Icon(Icons.search), hintText: 'Search'),
            onChanged: (v) => setState(() => _q = v),
          ),
          const SizedBox(height: 8),
          Expanded(
            child: _all == null
                ? const Center(child: CircularProgressIndicator())
                : ListView.builder(
                    itemCount: list.length,
                    itemBuilder: (_, i) => ListTile(
                      dense: true,
                      title: Text('${list[i]['name']}'),
                      onTap: () => Navigator.pop(context, list[i]),
                    ),
                  ),
          ),
        ]),
      ),
      actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel'))],
    );
  }
}

class _AppEditDialog extends StatefulWidget {
  const _AppEditDialog({required this.client, this.draft, this.existing, required this.groups});
  final DroidDeckClient client;
  final Map<String, dynamic>? draft;
  final KzApp? existing;
  final List<String> groups;

  @override
  State<_AppEditDialog> createState() => _AppEditDialogState();
}

class _AppEditDialogState extends State<_AppEditDialog> {
  late final TextEditingController _name;
  late final TextEditingController _group;
  late bool _favorite;
  String? _iconB64; // ícone atual (do programa ou o trocado)
  String? _customIcon; // só preenchido quando o usuário troca
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    final e = widget.existing;
    _name = TextEditingController(text: e?.name ?? '${widget.draft?['name'] ?? ''}');
    _group = TextEditingController(text: e?.group ?? '');
    _favorite = e?.favorite ?? false;
    _iconB64 = e?.icon != null ? base64Encode(e!.icon!) : widget.draft?['iconBase64'] as String?;
  }

  @override
  void dispose() {
    _name.dispose();
    _group.dispose();
    super.dispose();
  }

  Future<void> _pickIcon() async {
    final f = await ImagePicker().pickImage(source: ImageSource.gallery);
    if (f == null) return;
    final bytes = await f.readAsBytes();
    setState(() {
      _customIcon = base64Encode(bytes);
      _iconB64 = _customIcon;
    });
  }

  Future<void> _save() async {
    if (_name.text.trim().isEmpty) return;
    setState(() => _saving = true);
    try {
      await widget.client.saveApp(
        id: widget.existing?.id,
        name: _name.text.trim(),
        path: widget.existing == null ? '${widget.draft?['path']}' : null,
        group: _group.text.trim(),
        favorite: _favorite,
        iconBase64: _customIcon,
      );
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) {
        setState(() => _saving = false);
        kzToast(context, friendlyError(e));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final path = widget.existing?.path ?? '${widget.draft?['path'] ?? ''}';
    return AlertDialog(
      title: Text(widget.existing == null ? 'Add Application' : 'Edit Application'),
      content: SizedBox(
        width: 440,
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Container(
              width: 84,
              height: 84,
              decoration: BoxDecoration(
                color: scheme.surfaceContainer,
                borderRadius: BorderRadius.circular(16),
              ),
              padding: const EdgeInsets.all(10),
              child: _iconB64 != null
                  ? Image.memory(base64Decode(_iconB64!), fit: BoxFit.contain, filterQuality: FilterQuality.medium)
                  : Icon(Icons.apps_rounded, size: 40, color: scheme.primary),
            ),
            const SizedBox(width: 16),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                OutlinedButton.icon(
                    onPressed: _pickIcon,
                    icon: const Icon(Icons.image_outlined, size: 18),
                    label: const Text('Change icon')),
                if (_customIcon != null)
                  TextButton(
                    onPressed: () => setState(() {
                      _customIcon = null;
                      _iconB64 = widget.existing?.icon != null ? base64Encode(widget.existing!.icon!) : widget.draft?['iconBase64'] as String?;
                    }),
                    child: const Text('Use the program icon'),
                  ),
              ]),
            ),
          ]),
          const SizedBox(height: 16),
          TextField(
            controller: _name,
            decoration: const InputDecoration(labelText: 'Name', border: OutlineInputBorder()),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _group,
            decoration: InputDecoration(
              labelText: 'Group (optional)',
              border: const OutlineInputBorder(),
              helperText: widget.groups.isEmpty ? null : 'Existing: ${widget.groups.join(', ')}',
            ),
          ),
          const SizedBox(height: 4),
          SwitchListTile(
            contentPadding: EdgeInsets.zero,
            title: const Text('Show on the dashboard (favorite)'),
            value: _favorite,
            onChanged: (v) => setState(() => _favorite = v),
          ),
          Text(path,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 11, color: scheme.onSurface.withValues(alpha: 0.5))),
        ]),
      ),
      actions: [
        TextButton(onPressed: _saving ? null : () => Navigator.pop(context, false), child: const Text('Cancel')),
        FilledButton(onPressed: _saving ? null : _save, child: const Text('Save')),
      ],
    );
  }
}
