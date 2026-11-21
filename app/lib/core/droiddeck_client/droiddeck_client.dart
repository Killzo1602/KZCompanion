
import 'package:droiddeck/core/i18n/i18n.dart';
import 'package:dio/dio.dart';
import 'models/mixer_data.dart';
import 'models/audio_target.dart';
import 'models/mixer_entity.dart';
import 'models/mixer_master.dart';
import 'models/media_session.dart';

class DroidDeckClient {
  final Dio _dio;
  final String baseUrl;

  DroidDeckClient({
    required String baseUrl,
    Dio? dio,
  })  : _dio = dio ?? Dio(),
        baseUrl = baseUrl {
    _dio.options.baseUrl = baseUrl;
    // O PC traduz o que manda (tipos de ação, erros) pelo idioma do app.
    _dio.interceptors.add(InterceptorsWrapper(onRequest: (options, handler) {
      options.headers['Accept-Language'] = AppLanguage.header;
      handler.next(options);
    }));
  }

  void setBaseUrl(String url) {
    _dio.options.baseUrl = url;
  }

  /// Define (ou remove) a chave de API enviada em todas as requisições (header X-API-KEY).
  void setApiKey(String? key) {
    if (key == null || key.isEmpty) {
      _dio.options.headers.remove('X-API-KEY');
    } else {
      _dio.options.headers['X-API-KEY'] = key;
    }
  }

  Future<List<MixerEntity>> getOutputs() async {
    final response = await _dio.get('/api/v1/Mixer/out');
    return (response.data as List)
        .map((e) => MixerEntity.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<MixerEntity> getOutput(String id) async {
    final response = await _dio.get('/api/v1/Mixer/out/$id');
    return MixerEntity.fromJson(response.data as Map<String, dynamic>);
  }

  Future<MixerMaster> setOutput(String id, MixerData data) async {
    final response = await _dio.put(
      '/api/v1/Mixer/out/$id',
      data: data.toJson(),
    );
    return MixerMaster.fromJson(response.data as Map<String, dynamic>);
  }

  Future<List<MixerEntity>> getInputs() async {
    final response = await _dio.get('/api/v1/Mixer/in');
    return (response.data as List)
        .map((e) => MixerEntity.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<MixerEntity> getInput(String id) async {
    final response = await _dio.get('/api/v1/Mixer/in/$id');
    return MixerEntity.fromJson(response.data as Map<String, dynamic>);
  }

  Future<MixerMaster> setInput(String id, MixerData data) async {
    final response = await _dio.put(
      '/api/v1/Mixer/in/$id',
      data: data.toJson(),
    );
    return MixerMaster.fromJson(response.data as Map<String, dynamic>);
  }

  Future<Map<String, dynamic>> mute(AudioTarget target) async {
    final response = await _dio.post(
      '/api/Software/mute',
      data: target.toJson(),
    );
    return response.data as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> toggleMute(AudioTarget target) async {
    final response = await _dio.post(
      '/api/Software/toggle-mute',
      data: target.toJson(),
    );
    return response.data as Map<String, dynamic>;
  }

  /// Estado de mute atual de um processo (null se não houver sessão de áudio).
  Future<bool?> getMuteState(String process) async {
    final response = await _dio.get('/api/Software/mute-state/$process');
    final data = response.data;
    if (data is Map && data['muted'] is bool) return data['muted'] as bool;
    return null;
  }

  /// Estado atual do Discord (connected/mute/deaf/channelId/channelName/
  /// inputVolume/outputVolume/voiceMode/participants) ou null se indisponível.
  Future<Map<String, dynamic>?> getDiscordState() async {
    try {
      final response = await _dio.get('/api/discord/state');
      return (response.data as Map).cast<String, dynamic>();
    } catch (_) {
      return null;
    }
  }

  /// Servidores do Discord do usuário ([{id, name}]).
  Future<List<Map<String, String>>> getDiscordGuilds() async {
    try {
      final r = await _dio.get('/api/discord/guilds');
      return (r.data as List)
          .map<Map<String, String>>(
              (e) => {'id': '${e['id']}', 'name': '${e['name'] ?? ''}'})
          .toList();
    } catch (_) {
      return [];
    }
  }

  /// Canais de voz de um servidor ([{id, name}]).
  Future<List<Map<String, String>>> getDiscordChannels(String guildId) async {
    try {
      final r = await _dio.get('/api/discord/channels/$guildId');
      return (r.data as List)
          .map<Map<String, String>>(
              (e) => {'id': '${e['id']}', 'name': '${e['name'] ?? ''}'})
          .toList();
    } catch (_) {
      return [];
    }
  }

  /// Salva as credenciais do app Discord (Client ID + Secret) no PC.
  Future<void> setDiscordConfig(String clientId, String clientSecret) async {
    await _dio.post('/api/discord/config',
        data: {'clientId': clientId, 'clientSecret': clientSecret});
  }

  /// Conecta ao Discord (abre o popup de autorização no PC na 1ª vez).
  /// Lança [Exception] com mensagem legível em caso de erro.
  Future<Map<String, dynamic>> connectDiscord() async {
    try {
      final r = await _dio.post('/api/discord/connect');
      return (r.data as Map).cast<String, dynamic>();
    } on DioException catch (e) {
      final data = e.response?.data;
      final msg = (data is Map && data['error'] != null)
          ? '${data['error']}'
          : tr('Falha ao conectar — o Discord está aberto no PC?');
      throw Exception(msg);
    }
  }

  /// Sons da soundboard NATIVA do Discord ([{soundId, name, guildId, emojiName}]).
  Future<List<Map<String, String>>> getDiscordSoundboardSounds() async {
    try {
      final r = await _dio.get('/api/discord/soundboard-sounds');
      return (r.data as List)
          .map<Map<String, String>>((e) => {
                'soundId': '${e['soundId'] ?? ''}',
                'name': '${e['name'] ?? ''}',
                'guildId': '${e['guildId'] ?? ''}',
                'emojiName': '${e['emojiName'] ?? ''}',
              })
          .toList();
    } catch (_) {
      return [];
    }
  }

  /// Toca um som da soundboard nativa do Discord no canal de voz atual.
  Future<void> playDiscordSoundboard(String soundId, String? guildId) async {
    await _dio.post('/api/discord/play-soundboard',
        data: {'soundId': soundId, 'guildId': guildId});
  }

  // Media Control methods
  Future<List<MediaSession>> getMediaSessions() async {
    final response = await _dio.get('/api/v1/Media/sessions');
    return (response.data as List)
        .map((e) => MediaSession.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<MediaSession> getMediaSession(String id) async {
    final response = await _dio.get('/api/v1/Media/sessions/$id');
    return MediaSession.fromJson(response.data as Map<String, dynamic>);
  }

  /// Envia um comando de transporte e devolve o que o backend aplicou:
  /// `sessionId` (pode diferir do pedido, quando o id salvo nao existe mais) e
  /// `playing` (estado logo apos o comando). Mapa vazio se a resposta vier sem corpo.
  Future<Map<String, dynamic>> sendMediaCommand(String id, String command) async {
    final response = await _dio.post(
      '/api/v1/Media/sessions/$id/command',
      data: {'command': command},
    );
    final data = response.data;
    return data is Map<String, dynamic> ? data : <String, dynamic>{};
  }

  /// GET genérico para as listas de opções que o schema aponta (cenas, filtros...).
  Future<dynamic> getJson(String path) async {
    final response = await _dio.get(path);
    return response.data;
  }

  Future<dynamic> postJson(String path, [Object? body]) async {
    final response = await _dio.post(path, data: body);
    return response.data;
  }

  Future<List<int>> getBytes(String path) async {
    final response = await _dio.get<List<int>>(path,
        options: Options(responseType: ResponseType.bytes));
    return response.data ?? const [];
  }

  Future<dynamic> putJson(String path, [Object? body]) async {
    final response = await _dio.put(path, data: body);
    return response.data;
  }

  Future<dynamic> deleteJson(String path) async {
    final response = await _dio.delete(path);
    return response.data;
  }

  /// A chave atual é aceita pelo PC? (falha de rede também conta como não)
  Future<bool> checkKey() async {
    try {
      await _dio.get('/api/system/info');
      return true;
    } catch (_) {
      return false;
    }
  }

  /// Nome e Windows do PC (endpoint anonimo, o mesmo usado para checar conectividade).
  Future<Map<String, dynamic>> getPing() async {
    final r = await _dio.get('/api/ping');
    return (r.data as Map).cast<String, dynamic>();
  }

  // ---- KZ Companion: mixer de audio do Windows ----

  Future<Map<String, dynamic>> getAudio() async {
    final r = await _dio.get('/api/audio');
    return (r.data as Map).cast<String, dynamic>();
  }

  Future<void> setAudioMaster({int? volume, bool? mute}) async {
    await _dio.put('/api/audio/master', data: {'volume': volume, 'mute': mute});
  }

  Future<void> setAudioSession(String id, {int? volume, bool? mute}) async {
    await _dio.put('/api/audio/sessions/$id', data: {'volume': volume, 'mute': mute});
  }

  /// Varias sessoes de uma vez (o grupo de um aplicativo); cada uma continua com o seu id original.
  Future<Map<String, dynamic>> setAudioSessions(List<String> ids, {int? volume, bool? mute}) async {
    final r = await _dio.put('/api/audio/sessions', data: {'ids': ids, 'volume': volume, 'mute': mute});
    return (r.data as Map).cast<String, dynamic>();
  }

  /// Mudo do microfone padrao do Windows (so ele). Devolve o estado confirmado pelo PC.
  Future<Map<String, dynamic>> setAudioMic(bool mute) async {
    final r = await _dio.put('/api/audio/mic', data: {'mute': mute});
    return (r.data as Map).cast<String, dynamic>();
  }

  /// Deafen: muta saida + microfone e restaura o mudo anterior ao desligar. Devolve o estado confirmado.
  Future<Map<String, dynamic>> setDeafen(bool enabled) async {
    final r = await _dio.post('/api/audio/deafen', data: {'enabled': enabled});
    return (r.data as Map).cast<String, dynamic>();
  }

  // ---- KZ Companion: biblioteca de aplicativos e energia ----

  Future<List<Map<String, dynamic>>> getApps() async {
    final r = await _dio.get('/api/apps');
    return (r.data as List).map((e) => (e as Map).cast<String, dynamic>()).toList();
  }

  Future<void> launchApp(String id) async {
    await _dio.post('/api/apps/$id/launch');
  }

  /// Abre o seletor de arquivos do Windows NO PC. Null = o usuário cancelou.
  Future<Map<String, dynamic>?> pickApp() async {
    final r = await _dio.post('/api/apps/pick',
        options: Options(receiveTimeout: const Duration(minutes: 5)));
    if (r.statusCode == 204 || r.data == null || r.data == '') return null;
    return (r.data as Map).cast<String, dynamic>();
  }

  /// Aplicativos instalados (Menu Iniciar, programas registrados, apps da Loja) com "added".
  Future<List<Map<String, dynamic>>> getInstalledApps({bool refresh = false}) async {
    final r = await _dio.get('/api/apps/installed', queryParameters: {if (refresh) 'refresh': true});
    return (r.data as List).map((e) => (e as Map).cast<String, dynamic>()).toList();
  }

  Future<Map<String, dynamic>> previewApp(String path) async {
    final r = await _dio.post('/api/apps/preview', data: {'path': path});
    return (r.data as Map).cast<String, dynamic>();
  }

  Future<void> saveApp({
    String? id,
    required String name,
    String? path,
    String group = '',
    bool favorite = false,
    String? iconBase64,
  }) async {
    final body = {
      'name': name,
      'path': path,
      'group': group,
      'favorite': favorite,
      'iconBase64': iconBase64,
    };
    if (id == null) {
      await _dio.post('/api/apps', data: body);
    } else {
      await _dio.put('/api/apps/$id', data: body);
    }
  }

  Future<String?> getInstalledIcon(String key) async {
    final r = await _dio.get('/api/apps/installed/$key/icon');
    return (r.data as Map)['iconBase64'] as String?;
  }

  /// Adiciona um item da lista de instalados pela chave (o PC resolve caminho/AUMID).
  Future<void> addInstalled(String key, {String group = '', bool favorite = false}) async {
    await _dio.post('/api/apps/installed/$key/add', data: {'group': group, 'favorite': favorite});
  }

  /// Ordem dos aplicativos (favoritos incluidos). So o configurador no PC pode gravar.
  Future<void> reorderApps(List<String> ids) async {
    await _dio.put('/api/apps/order', data: {'ids': ids});
  }

  Future<void> deleteApp(String id) async {
    await _dio.delete('/api/apps/$id');
  }

  /// lock | sleep | restart | shutdown | cancel (restart/shutdown exigem [confirm]).
  Future<void> power(String operation, {bool confirm = false}) async {
    await _dio.post('/api/power', data: {'operation': operation, 'confirm': confirm});
  }
}
