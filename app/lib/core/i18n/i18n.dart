import 'package:signals_flutter/signals_flutter.dart';

class AppLanguage {
  AppLanguage._();

  static final current = signal('en');

  static String get header => current.value;
}

String tr(String text, [Map<String, Object?> args = const {}]) {
  var s = text;
  args.forEach((k, v) => s = s.replaceAll('{$k}', '${v ?? ''}'));
  return s;
}
