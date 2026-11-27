import 'dart:async';
import 'dart:js_interop';
import 'dart:js_interop_unsafe';

import 'package:web/web.dart' as web;

class DroppedFiles {
  const DroppedFiles(this.paths, this.rejected, [this.unresolved = const [], this.unsupported = false]);
  final List<String> paths;
  final List<String> rejected;

  final List<String> unresolved;

  final bool unsupported;
}

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
