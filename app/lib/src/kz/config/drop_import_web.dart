import 'dart:async';
import 'dart:js_interop';
import 'dart:js_interop_unsafe';

import 'package:web/web.dart' as web;

class DroppedFiles {
  const DroppedFiles(this.paths, this.rejected);
  final List<String> paths;
  final List<String> rejected;
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
      final list = (e as web.DragEvent).dataTransfer?.files;
      if (wv == null || list == null || list.length == 0) return;
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
            ));
          }
        }).toJS);
  }
}
