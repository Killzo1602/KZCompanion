// Arrastar e soltar .exe/.lnk no configurador (WebView2). Fora do navegador nao faz nada.
export 'drop_import_stub.dart' if (dart.library.js_interop) 'drop_import_web.dart';
