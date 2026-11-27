import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart' show kIsWeb;

class UsbRoute {
  UsbRoute._();

  static const usbBaseUrl = 'http://127.0.0.1:4787';

  static String? _lanBaseUrl;

  static bool isUsb(String baseUrl) => baseUrl == usbBaseUrl;

  static Future<String> pick(String baseUrl, {String? apiKey}) async {
    if (kIsWeb) return baseUrl;
    final lan = isUsb(baseUrl) ? (_lanBaseUrl ?? baseUrl) : baseUrl;
    if (!isUsb(lan)) _lanBaseUrl = lan;
    if (apiKey == null || apiKey.isEmpty) return lan;
    try {
      await Dio(BaseOptions(
        connectTimeout: const Duration(milliseconds: 600),
        receiveTimeout: const Duration(seconds: 2),
        headers: {'X-API-Key': apiKey},
      )).get('$usbBaseUrl/api/system/info');
      return usbBaseUrl;
    } catch (_) {
      return lan;
    }
  }
}
