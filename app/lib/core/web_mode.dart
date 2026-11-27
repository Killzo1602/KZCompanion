import 'package:flutter/foundation.dart' show kIsWeb;

class WebMode {
  WebMode._();

  static bool get _loopback {
    final host = Uri.base.host;
    return host == 'localhost' || host == '127.0.0.1' || host == '::1' || host == '[::1]';
  }

  static final bool configurator = kIsWeb && _loopback;

  static final bool deck = kIsWeb && !configurator;
}
