import 'package:flutter/foundation.dart' show kIsWeb;

/// Os dois jeitos de o app rodar no navegador.
///
/// - Configurador: aberto no próprio PC (localhost). Gerencia aplicativos, integrações e
///   aparência; a chave de API vem pelo loopback.
/// - Painel no navegador: aberto de outro aparelho da rede (iPhone, tablet, outro PC).
///   Funciona como o app do celular; a chave vem do QR "Abrir no navegador" da janela
///   de pareamento.
class WebMode {
  WebMode._();

  static bool get _loopback {
    final host = Uri.base.host;
    return host == 'localhost' || host == '127.0.0.1' || host == '::1' || host == '[::1]';
  }

  /// Navegador no PC, configurando o KZ Companion.
  static final bool configurator = kIsWeb && _loopback;

  /// Navegador usado como painel (no lugar do app de celular).
  static final bool deck = kIsWeb && !configurator;
}
