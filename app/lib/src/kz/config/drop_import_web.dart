import 'dart:async';
import 'dart:js_interop';
import 'dart:js_interop_unsafe';

import 'package:web/web.dart' as web;

class DroppedFiles {
  const DroppedFiles(this.paths, this.rejected, [this.unresolved = const []]);
  final List<String> paths;
  final List<String> rejected;

  /// Itens soltos sem caminho de arquivo (ex.: apps do Menu Iniciar): so o nome; abre o seletor.
  final List<String> unresolved;
}

/// Arrastar arquivos do Explorer para o configurador (WebView2). O navegador so entrega File sem
/// caminho; o WebView2 passa o caminho real ao app do PC (postMessageWithAdditionalObjects), que
/// responde com a lista validada. Sem WebView2 (navegador comum) o arrastar e simplesmente ignorado.
class DropImport {
  static final _files = StreamController<DroppedFiles>.broadcast();
  static final _dragging = StreamController<bool>.broadcast();
  static bool _started = false;

  static Stream<DroppedFiles> get files => _files.stream;
  static Stream<bool> get dragging => _dragging.stream;

  static JSObject? get _webview {
    final chrome = globalContext['chrome'];
    if (chrome == null || !chrome.isA<JSObject>()) return null;
    final wv = (chrome as JSObject)['webview'];
    return (wv != null && wv.isA<JSObject>()) ? wv as JSObject : null;
  }

  static void start() {
    if (_started) return;
    _started = true;
    final doc = web.document;

    doc.addEventListener('dragover', ((web.Event e) {
      e.preventDefault(); // sem isso o WebView2 abriria o arquivo no lugar de soltar
      _dragging.add(true);
    }).toJS);
    doc.addEventListener('dragleave', ((web.Event e) {
      final de = e as web.DragEvent;
      if (de.relatedTarget == null) _dragging.add(false);
    }).toJS);
    doc.addEventListener('drop', ((web.Event e) {
      e.preventDefault();
      _dragging.add(false);
      final wv = _webview;
      final dt = (e as web.DragEvent).dataTransfer;
      final list = dt?.files;
      if (wv == null || dt == null) return;
      if (list == null || list.length == 0) {
        // Sem arquivos (o Menu Iniciar do Windows 11 pode nao oferecer um): usa o texto/URI, se vier.
        final names = <String>{};
        for (final type in const ['text/uri-list', 'text/plain']) {
          final raw = dt.getData(type);
          for (final line in raw.split(RegExp(r'[\r\n]+'))) {
            final t = line.trim();
            if (t.isEmpty || t.startsWith('#')) continue;
            names.add(Uri.decodeFull(t.split(RegExp(r'[\\/]')).last).replaceAll(RegExp(r'\.(lnk|exe|url)$', caseSensitive: false), ''));
          }
        }
        if (names.isNotEmpty) _files.add(DroppedFiles(const [], const [], names.toList()));
        return;
      }
      final array = (globalContext['Array'] as JSObject).callMethod('from'.toJS, list);
      wv.callMethod('postMessageWithAdditionalObjects'.toJS, {'type': 'files-dropped'}.jsify(), array);
    }).toJS);

    _webview?.callMethod(
        'addEventListener'.toJS,
        'message'.toJS,
        ((web.MessageEvent e) {
          final data = e.data.dartify();
          if (data is Map && data['type'] == 'dropped') {
            _files.add(DroppedFiles(
              [for (final p in (data['paths'] as List? ?? const [])) '$p'],
              [for (final p in (data['rejected'] as List? ?? const [])) '$p'],
              [for (final p in (data['unresolved'] as List? ?? const [])) '$p'],
            ));
          }
        }).toJS);
  }
}
