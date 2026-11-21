import 'dart:async';
import 'dart:js_interop';
import 'dart:js_interop_unsafe';

import 'package:web/web.dart' as web;

class DroppedFiles {
  const DroppedFiles(this.paths, this.rejected, [this.unresolved = const [], this.unsupported = false]);
  final List<String> paths;
  final List<String> rejected;

  /// Itens soltos sem caminho de arquivo (ex.: apps do Menu Iniciar): so o nome; abre o seletor.
  final List<String> unresolved;

  /// O item solto nao tem nada utilizavel (nem arquivo nem nome): orienta a usar o seletor.
  final bool unsupported;
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

  /// So escuta o host. O arrasto em si NAO passa pela pagina: o app do PC trata o OLE nativamente,
  /// valida e manda JSON simples ({type: dragging|dropped}).
  static void start() {
    if (_started) return;
    _started = true;
    _webview?.callMethod(
        'addEventListener'.toJS,
        'message'.toJS,
        ((web.MessageEvent e) {
          final data = e.data.dartify();
          if (data is! Map) return;
          if (data['type'] == 'dragging') {
            _dragging.add(data['on'] == true);
          } else if (data['type'] == 'dropped') {
            _files.add(DroppedFiles(
              [for (final p in (data['paths'] as List? ?? const [])) '$p'],
              [for (final p in (data['rejected'] as List? ?? const [])) '$p'],
              [for (final p in (data['unresolved'] as List? ?? const [])) '$p'],
              data['unsupported'] == true,
            ));
          }
        }).toJS);
  }
}
