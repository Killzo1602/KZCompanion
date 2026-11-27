import 'package:dio/dio.dart';

import '../i18n/i18n.dart';

String friendlyError(Object error) {
  if (error is DioException) {
    final data = error.response?.data;
    if (data is Map && data['error'] is String) return data['error'] as String;
    final code = error.response?.statusCode;
    if (code == 401 || code == 403) {
      return tr('The PC rejected this device\'s key. Pair again.');
    }
    switch (error.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.receiveTimeout:
      case DioExceptionType.sendTimeout:
        return tr('The PC took too long to answer. Try again.');
      case DioExceptionType.connectionError:
        return tr('The PC didn\'t answer. Check that KZ Companion is open on it and that both are on the same network.');
      default:
        break;
    }
    if (code != null && code >= 500) {
      return tr('KZ Companion on the PC ran into a problem. Try again; if it keeps happening, restart KZ Companion on the PC.');
    }
  }
  return tr('Something went wrong. Try again.');
}
