import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/config/kz_config_shell.dart';
import 'package:droiddeck/src/kz/kz_shell.dart';
import 'package:flutter/material.dart';

/// Ponto de entrada depois do splash: o configurador no navegador do próprio PC, ou o
/// painel do KZ Companion (celular, ou navegador de outro aparelho).
class HomePage extends StatelessWidget {
  const HomePage({super.key});

  @override
  Widget build(BuildContext context) =>
      WebMode.configurator ? const KzConfigShell() : const KzShell();
}
