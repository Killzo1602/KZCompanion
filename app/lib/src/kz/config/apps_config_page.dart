import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

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
  bool _elevated = false;
  StreamSubscription<DroppedFiles>? _dropSub;
  StreamSubscription<bool>? _dragSub;

  @override
  void initState() {
    super.initState();
    _load();
    _client.getPing().then((p) {
      if (mounted) setState(() => _elevated = p['elevated'] == true);
    }).catchError((_) {});
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
    // Itens sem caminho (Menu Iniciar do Windows 11 / apps da Loja): o seletor de instalados resolve pelo nome.
    final unresolved = dropped.unresolved.isEmpty ? null : dropped.unresolved.first;
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
    if (dropped.unsupported && dropped.paths.isEmpty && mounted) {
      kzToast(context, 'This item cannot be dropped here. Pick the application from the installed list.');
      await _addFromInstalled();
      return;
    }
    if (unresolved != null && mounted) {
      kzToast(context, 'Windows did not provide a file for "$unresolved". Pick it from the installed list.');
      await _addFromInstalled(query: unresolved);
    }
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

  Future<void> _addFromInstalled({String query = ''}) async {
    await showDialog<void>(
      context: context,
      builder: (_) => _InstalledDialog(client: _client, initialQuery: query, onAdded: _load),
    );
    if (mounted) _load();
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
                      if (_elevated) _elevatedNotice(scheme),
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

  /// Windows (UIPI) nao deixa arrastar do Explorer/Menu Iniciar para uma janela de administrador.
  Widget _elevatedNotice(ColorScheme scheme) => Container(
        margin: const EdgeInsets.only(bottom: 16),
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: const Color(0xFFFBBF24).withValues(alpha: 0.10),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: const Color(0xFFFBBF24).withValues(alpha: 0.5)),
        ),
        child: const Row(children: [
          Icon(Icons.shield_outlined, color: Color(0xFFFBBF24)),
          SizedBox(width: 10),
          Expanded(
            child: Text(
              'KZ Companion is running as administrator, so Windows blocks dragging files into this window. '
              'Use "Installed apps" or Add Application, or restart KZ Companion normally and start the sensor '
              'helper from the tray menu for CPU sensors.',
              style: TextStyle(fontSize: 12.5),
            ),
          ),
        ]),
      );

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
  const _InstalledDialog({required this.client, this.initialQuery = '', required this.onAdded});
  final DroidDeckClient client;
  final String initialQuery;
  final Future<void> Function() onAdded;

  @override
  State<_InstalledDialog> createState() => _InstalledDialogState();
}

/// Seletor de aplicativos instalados: Menu Iniciar (usuario e sistema), programas registrados e
/// apps da Loja. Busca, icone, Adicionar e "Added". Cada item abre pelo mecanismo certo (atalho
/// com argumentos, .exe ou AppUserModelID); duplicatas sao reconhecidas pela identidade do programa.
class _InstalledDialogState extends State<_InstalledDialog> {
  List<Map<String, dynamic>>? _all;
  late final TextEditingController _search;
  final Map<String, Uint8List?> _icons = {};
  final Set<String> _loadingIcon = {};
  final Set<String> _adding = {};

  @override
  void initState() {
    super.initState();
    _search = TextEditingController(text: widget.initialQuery);
    _load();
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _load({bool refresh = false}) async {
    try {
      final v = await widget.client.getInstalledApps(refresh: refresh);
      if (mounted) setState(() => _all = v);
    } catch (_) {
      if (mounted) setState(() => _all = []);
    }
  }

  Future<void> _icon(String key) async {
    if (_icons.containsKey(key) || !_loadingIcon.add(key)) return;
    try {
      final b64 = await widget.client.getInstalledIcon(key);
      if (mounted) setState(() => _icons[key] = (b64 == null || b64.isEmpty) ? null : base64Decode(b64));
    } catch (_) {
      if (mounted) setState(() => _icons[key] = null);
    }
  }

  Future<void> _add(Map<String, dynamic> item) async {
    final key = '${item['key']}';
    setState(() => _adding.add(key));
    try {
      await widget.client.addInstalled(key);
      item['added'] = true;
      kzToast(context, 'Added ${item['name']}');
      await widget.onAdded();
    } on DioException catch (e) {
      final msg = e.response?.data is Map ? '${(e.response!.data as Map)['error']}' : friendlyError(e);
      if (e.response?.statusCode == 409) item['added'] = true;
      if (mounted) kzToast(context, msg);
    } catch (e) {
      if (mounted) kzToast(context, friendlyError(e));
    } finally {
      if (mounted) setState(() => _adding.remove(key));
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final q = _search.text.trim().toLowerCase();
    final list = (_all ?? [])
        .where((a) => q.isEmpty || '${a['name']}'.toLowerCase().contains(q) || '${a['detail']}'.toLowerCase().contains(q))
        .toList();
    return AlertDialog(
      title: Row(children: [
        const Expanded(child: Text('Installed applications')),
        IconButton(
          tooltip: 'Refresh',
          onPressed: () {
            setState(() => _all = null);
            _icons.clear();
            _loadingIcon.clear();
            _load(refresh: true);
          },
          icon: const Icon(Icons.refresh_rounded),
        ),
      ]),
      content: SizedBox(
        width: 560,
        height: 480,
        child: Column(children: [
          TextField(
            controller: _search,
            autofocus: true,
            decoration: const InputDecoration(prefixIcon: Icon(Icons.search), hintText: 'Search installed applications'),
            onChanged: (_) => setState(() {}),
          ),
          const SizedBox(height: 6),
          Align(
            alignment: Alignment.centerLeft,
            child: Text(_all == null ? 'Looking for applications…' : '${list.length} of ${_all!.length} applications',
                style: TextStyle(fontSize: 11.5, color: scheme.onSurface.withValues(alpha: 0.55))),
          ),
          const SizedBox(height: 4),
          Expanded(
            child: _all == null
                ? const Center(child: CircularProgressIndicator())
                : list.isEmpty
                    ? const Center(child: Text('No matching applications'))
                    : ListView.builder(
                        itemCount: list.length,
                        itemExtent: 56,
                        itemBuilder: (_, i) {
                          final item = list[i];
                          final key = '${item['key']}';
                          _icon(key); // so as linhas visiveis pedem o icone
                          final added = item['added'] == true;
                          final icon = _icons[key];
                          final detail = '${item['detail']}'.isNotEmpty ? '${item['detail']}' : '${item['source']}';
                          return ListTile(
                            dense: true,
                            leading: SizedBox(
                              width: 36,
                              height: 36,
                              child: icon != null
                                  ? Image.memory(icon, fit: BoxFit.contain, filterQuality: FilterQuality.medium)
                                  : Icon(item['kind'] == 'packaged' ? Icons.store_rounded : Icons.apps_rounded,
                                      color: scheme.primary.withValues(alpha: 0.6)),
                            ),
                            title: Text('${item['name']}', maxLines: 1, overflow: TextOverflow.ellipsis),
                            subtitle: Text(
                              item['kind'] == 'packaged' ? 'Microsoft Store app' : detail,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(fontSize: 11),
                            ),
                            trailing: added
                                ? Chip(
                                    visualDensity: VisualDensity.compact,
                                    avatar: Icon(Icons.check_rounded, size: 16, color: scheme.primary),
                                    label: const Text('Added', style: TextStyle(fontSize: 12)),
                                  )
                                : FilledButton.tonal(
                                    onPressed: _adding.contains(key) ? null : () => _add(item),
                                    child: _adding.contains(key)
                                        ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
                                        : const Text('Add'),
                                  ),
                          );
                        },
                      ),
          ),
        ]),
      ),
      actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('Done'))],
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
