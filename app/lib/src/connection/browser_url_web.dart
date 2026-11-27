import 'package:web/web.dart' as web;

void clearPairingFromUrl() => clearUrlParam('pair');

void clearUrlParam(String name) {
  final uri = Uri.base;
  final rest = Map<String, String>.from(uri.queryParameters)..remove(name);
  final query = rest.isEmpty ? '' : '?${Uri(queryParameters: rest).query}';
  final fragment = uri.fragment.isEmpty ? '' : '#${uri.fragment}';
  web.window.history.replaceState(null, '', '${uri.path}$query$fragment');
}

void setBrowserFullscreen(bool on) {
  try {
    final doc = web.document;
    if (on && doc.fullscreenElement == null) {
      doc.documentElement?.requestFullscreen();
    } else if (!on && doc.fullscreenElement != null) {
      doc.exitFullscreen();
    }
  } catch (_) {}
}
