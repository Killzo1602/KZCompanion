import 'package:flutter/material.dart';

/// Quanto do que o catalogo promete existe de verdade hoje. Nada aqui diz "funciona": o estado
/// "Connected" so aparece quando o PC realmente informa que esta conectado.
enum KzSupport {
  /// Tem tela de configuracao neste app e o codigo do PC conecta; ainda sem teste ao vivo.
  setupAvailable('Setup available', Icons.tune_rounded),

  /// O PC tem o codigo, mas o KZ Companion ainda nao tem tela nem painel para ele.
  backendOnly('Backend only', Icons.construction_rounded),

  /// Ideia viavel sem prova: depende de um teste no PC antes de virar integracao.
  candidate('Candidate', Icons.science_outlined),

  /// Parado de proposito (depende de algo que nao e nosso).
  onHold('On hold', Icons.pause_circle_outline_rounded);

  const KzSupport(this.label, this.icon);
  final String label;
  final IconData icon;
}

class KzIntegration {
  const KzIntegration({
    required this.id,
    required this.name,
    required this.icon,
    required this.support,
    required this.summary,
    required this.capabilities,
    required this.requirements,
    this.caveats = const [],
    this.verification = 'Reviewed against current documentation. Not tested with a live account yet.',
    this.backendName,
  });

  final String id;
  final String name;
  final IconData icon;
  final KzSupport support;
  final String summary;

  /// O que a integracao pretende oferecer (nao e promessa de que ja funciona).
  final List<String> capabilities;
  final List<String> requirements;
  final List<String> caveats;
  final String verification;

  /// Nome em /api/integrations, quando o PC a lista.
  final String? backendName;
}

/// Catalogo da auditoria da Fase 1 (docs/integration-audit-phase1.md). Ordem = prioridade.
class KzCatalog {
  static const priority = <KzIntegration>[
    KzIntegration(
      id: 'discord',
      name: 'Discord',
      icon: Icons.headset_mic_rounded,
      support: KzSupport.setupAvailable,
      summary: 'Voice controls for the Discord app on this PC.',
      capabilities: ['Mute and deafen', 'Voice channel and state', 'Input / output volume'],
      requirements: [
        'Discord desktop app running on the PC',
        'Your own Discord application (Client ID and Client Secret)',
        'Discord limits these voice permissions to approved apps. Its documentation allows '
            'unapproved apps for users on the app\'s tester list (up to 50): add your own account there',
        'Approve the prompt that Discord shows the first time',
      ],
      caveats: [
        'While KZ Companion changes voice settings, other apps cannot change them (Discord limit).',
        'The Discord soundboard is not offered: Discord does not document it for this interface.',
      ],
      backendName: 'discord',
    ),
    KzIntegration(
      id: 'spotify',
      name: 'Spotify',
      icon: Icons.music_note_rounded,
      support: KzSupport.setupAvailable,
      summary: 'Account login for Spotify playback control.',
      capabilities: ['Playback controls', 'Now playing', 'Playlists'],
      requirements: [
        'Spotify Premium (needed for playback control)',
        'Your own app in the Spotify Developer Dashboard (Client ID, no secret)',
        'Register this redirect address in that app: http://127.0.0.1:4787/api/integrations/spotify/callback',
        'Development Mode apps need a Premium owner and allow up to 5 users',
      ],
      caveats: [
        'You can sign in now, but the player panel is not in KZ Companion yet.',
        'Save-to-library uses an endpoint Spotify removed in February 2026 and needs an update.',
      ],
      backendName: 'spotify',
    ),
    KzIntegration(
      id: 'obs',
      name: 'OBS Studio',
      icon: Icons.videocam_rounded,
      support: KzSupport.setupAvailable,
      summary: 'Scenes, recording and streaming for OBS on this PC.',
      capabilities: ['Scene list and switching', 'Record and stream state', 'Audio inputs'],
      requirements: [
        'OBS Studio with the WebSocket server turned on (Tools, WebSocket Server Settings)',
        'The port (4455 by default) and the password, if you set one',
      ],
      caveats: ['Controls for the phone are not built yet; this connects and reads state.'],
      backendName: 'obs',
    ),
    KzIntegration(
      id: 'apple_music',
      name: 'Apple Music',
      icon: Icons.library_music_rounded,
      support: KzSupport.candidate,
      summary: 'Possible Windows media integration. Not a native Apple Music API.',
      capabilities: [
        'Detect the running app (unverified)',
        'Now playing through Windows media controls (unverified)',
        'Play / pause / skip through Windows media controls (unverified)',
        'App volume through the Windows mixer (expected to work)',
      ],
      requirements: [
        'Apple Music for Windows (Microsoft Store)',
        'Whether the app exposes Windows media controls has not been tested yet',
      ],
      caveats: [
        'Apple has no API to control the desktop app. MusicKit plays music inside your own app and does not apply here.',
        'No playlists, likes or library access.',
      ],
      verification: 'Nothing here has been tested. The check is planned for the media phase.',
    ),
  ];

  static const more = <KzIntegration>[
    KzIntegration(
      id: 'homeassistant',
      name: 'Home Assistant',
      icon: Icons.home_rounded,
      support: KzSupport.backendOnly,
      summary: 'Lights, switches and scenes.',
      capabilities: ['Device control', 'State monitoring'],
      requirements: ['A long-lived access token from your Home Assistant profile', 'The Home Assistant address'],
      caveats: ['Needs a device list stored by KZ Companion before it can be shown.'],
      backendName: 'homeassistant',
    ),
    KzIntegration(
      id: 'hue',
      name: 'Philips Hue',
      icon: Icons.lightbulb_outline_rounded,
      support: KzSupport.backendOnly,
      summary: 'Lights through the local Hue Bridge.',
      capabilities: ['Light and scene control'],
      requirements: ['Hue Bridge on the same network', 'Press the bridge button while pairing'],
      caveats: ['Uses the older local API (v1). Its long-term support is not verified.'],
      backendName: 'hue',
    ),
    KzIntegration(
      id: 'govee',
      name: 'Govee',
      icon: Icons.light_rounded,
      support: KzSupport.backendOnly,
      summary: 'Lights through the local network (LAN API).',
      capabilities: ['On / off, brightness, colour'],
      requirements: ['LAN Control turned on for each device in the Govee Home app'],
    ),
    KzIntegration(
      id: 'mqtt',
      name: 'MQTT',
      icon: Icons.hub_outlined,
      support: KzSupport.backendOnly,
      summary: 'Publish messages to an MQTT broker.',
      capabilities: ['Publish only'],
      requirements: ['Broker address and credentials'],
      backendName: 'mqtt',
    ),
    KzIntegration(
      id: 'twitch',
      name: 'Twitch',
      icon: Icons.live_tv_rounded,
      support: KzSupport.backendOnly,
      summary: 'Channel actions for streamers.',
      capabilities: ['Title, clips, markers, chat, polls'],
      requirements: ['Your own Twitch application (Client ID)'],
      backendName: 'twitch',
    ),
    KzIntegration(
      id: 'youtube',
      name: 'YouTube Live',
      icon: Icons.smart_display_rounded,
      support: KzSupport.backendOnly,
      summary: 'Live stream status and chat.',
      capabilities: ['Stream status', 'Chat messages'],
      requirements: ['Your own Google Cloud OAuth client'],
      backendName: 'youtube',
    ),
    KzIntegration(
      id: 'tuya',
      name: 'Tuya / Smart Life',
      icon: Icons.power_outlined,
      support: KzSupport.onHold,
      summary: 'Smart plugs and lights.',
      capabilities: ['Device control'],
      requirements: ['Smart Life or Tuya Smart app'],
      caveats: [
        'Pairing borrows Home Assistant\'s public registration. It stays off until KZ Companion has its own Tuya registration.',
      ],
    ),
  ];

  static List<KzIntegration> get all => [...priority, ...more];
}
