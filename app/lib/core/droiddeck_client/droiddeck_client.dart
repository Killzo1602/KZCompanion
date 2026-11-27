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
    _dio.interceptors.add(InterceptorsWrapper(onRequest: (options, handler) {
      options.headers['Accept-Language'] = AppLanguage.header;
      handler.next(options);
    }));
  }

  void setBaseUrl(String url) {
    _dio.options.baseUrl = url;
  }

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

  Future<bool?> getMuteState(String process) async {
    final response = await _dio.get('/api/Software/mute-state/$process');
    final data = response.data;
    if (data is Map && data['muted'] is bool) return data['muted'] as bool;
    return null;
  }

  Future<Map<String, dynamic>?> getDiscordState() async {
    try {
      final response = await _dio.get('/api/discord/state');
      return (response.data as Map).cast<String, dynamic>();
    } catch (_) {
      return null;
    }
  }

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

  Future<void> setDiscordConfig(String clientId, String clientSecret) async {
    await _dio.post('/api/discord/config',
        data: {'clientId': clientId, 'clientSecret': clientSecret});
  }

  Future<Map<String, dynamic>> connectDiscord() async {
    try {
      final r = await _dio.post('/api/discord/connect');
      return (r.data as Map).cast<String, dynamic>();
    } on DioException catch (e) {
      final data = e.response?.data;
      final msg = (data is Map && data['error'] != null)
          ? '${data['error']}'
          : tr('Failed to connect — is Discord open on the PC?');
      throw Exception(msg);
    }
  }

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

  Future<void> playDiscordSoundboard(String soundId, String? guildId) async {
    await _dio.post('/api/discord/play-soundboard',
        data: {'soundId': soundId, 'guildId': guildId});
  }

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

  Future<Map<String, dynamic>> sendMediaCommand(String id, String command) async {
    final response = await _dio.post(
      '/api/v1/Media/sessions/$id/command',
      data: {'command': command},
    );
    final data = response.data;
    return data is Map<String, dynamic> ? data : <String, dynamic>{};
  }

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

  Future<bool> checkKey() async {
    try {
      await _dio.get('/api/system/info');
      return true;
    } catch (_) {
      return false;
    }
  }

  Future<Map<String, dynamic>> getPing() async {
    final r = await _dio.get('/api/ping');
    return (r.data as Map).cast<String, dynamic>();
  }

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

  Future<Map<String, dynamic>> setAudioSessions(List<String> ids, {int? volume, bool? mute}) async {
    final r = await _dio.put('/api/audio/sessions', data: {'ids': ids, 'volume': volume, 'mute': mute});
    return (r.data as Map).cast<String, dynamic>();
  }

  Future<Map<String, dynamic>> setAudioMic(bool mute) async {
    final r = await _dio.put('/api/audio/mic', data: {'mute': mute});
    return (r.data as Map).cast<String, dynamic>();
  }

  Future<Map<String, dynamic>> setDeafen(bool enabled) async {
    final r = await _dio.post('/api/audio/deafen', data: {'enabled': enabled});
    return (r.data as Map).cast<String, dynamic>();
  }

  Future<List<Map<String, dynamic>>> getApps() async {
    final r = await _dio.get('/api/apps');
    return (r.data as List).map((e) => (e as Map).cast<String, dynamic>()).toList();
  }

  Future<List<Map<String, dynamic>>> getIntegrations() async {
    final r = await _dio.get('/api/integrations');
    return (r.data as List).map((e) => (e as Map).cast<String, dynamic>()).toList();
  }

  Future<Map<String, dynamic>> getIntegration(String name) async {
    final r = await _dio.get('/api/integrations/$name');
    return (r.data as Map).cast<String, dynamic>();
  }

  Future<Map<String, dynamic>> configureIntegration(String name, Map<String, String> fields) async {
    final r = await _dio.post('/api/integrations/$name', data: fields);
    return (r.data as Map).cast<String, dynamic>();
  }

  Future<Map<String, dynamic>> logoutIntegration(String name) async {
    final r = await _dio.delete('/api/integrations/$name');
    return (r.data as Map).cast<String, dynamic>();
  }

  Future<void> setObsConfig({required String host, required int port, String? password}) async {
    await _dio.post('/api/obs/config', data: {
      'host': host,
      'port': port,
      if (password != null && password.isNotEmpty) 'password': password,
    });
  }

  Future<Map<String, dynamic>> connectObs() async {
    final r = await _dio.post('/api/obs/connect');
    return (r.data as Map).cast<String, dynamic>();
  }

  Future<Map<String, dynamic>?> getObsState() async {
    try {
      final r = await _dio.get('/api/obs/state');
      return (r.data as Map).cast<String, dynamic>();
    } catch (_) {
      return null;
    }
  }

  Future<List<Map<String, dynamic>>> getRunningApps() async {
    final r = await _dio.get('/api/running-apps');
    final list = (r.data as Map)['apps'] as List? ?? const [];
    return list.map((e) => (e as Map).cast<String, dynamic>()).toList();
  }

  Future<List<int>> getRunningAppIcon(String id) => getBytes('/api/running-apps/$id/icon');

  Future<Map<String, dynamic>> runningAppAction(String id, String action,
      {String? windowId, bool confirm = false}) async {
    final r = await _dio.post('/api/running-apps/$id/$action', queryParameters: {
      if (windowId != null) 'windowId': windowId,
      if (confirm) 'confirm': 'true',
    });
    return (r.data as Map?)?.cast<String, dynamic>() ?? const {};
  }

  Future<void> launchApp(String id) async {
    await _dio.post('/api/apps/$id/launch');
  }

  Future<Map<String, dynamic>?> pickApp() async {
    final r = await _dio.post('/api/apps/pick',
        options: Options(receiveTimeout: const Duration(minutes: 5)));
    if (r.statusCode == 204 || r.data == null || r.data == '') return null;
    return (r.data as Map).cast<String, dynamic>();
  }

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

  Future<void> addInstalled(String key, {String group = '', bool favorite = false}) async {
    await _dio.post('/api/apps/installed/$key/add', data: {'group': group, 'favorite': favorite});
  }

  Future<void> reorderApps(List<String> ids) async {
    await _dio.put('/api/apps/order', data: {'ids': ids});
  }

  Future<void> deleteApp(String id) async {
    await _dio.delete('/api/apps/$id');
  }

  Future<List<Map<String, dynamic>>> getAudioOutputs() async {
    final r = await _dio.get('/api/audio/outputs');
    return (r.data as List).map((e) => (e as Map).cast<String, dynamic>()).toList();
  }

  Future<void> setAudioOutput(String id) async {
    await _dio.put('/api/audio/output', data: {'id': id});
  }

  Future<List<Map>> getAiSessions() async {
    final r = await _dio.get('/api/ai-sessions');
    return ((r.data as Map)['sessions'] as List).cast<Map>();
  }

  Future<Map<String, dynamic>> getGitHub() async =>
      ((await _dio.get('/api/dev/github')).data as Map).cast<String, dynamic>();

  Future<Map<String, dynamic>> getDocker() async =>
      ((await _dio.get('/api/dev/docker')).data as Map).cast<String, dynamic>();

  Future<void> dockerCommand(String id, String operation) async {
    await _dio.post('/api/dev/docker/$id/$operation');
  }

  Future<List<Map<String, dynamic>>> getAiUsage() async {
    final r = await _dio.get('/api/ai-usage');
    return ((r.data as Map)['providers'] as List).map((e) => (e as Map).cast<String, dynamic>()).toList();
  }

  Future<void> power(String operation, {bool confirm = false}) async {
    await _dio.post('/api/power', data: {'operation': operation, 'confirm': confirm});
  }
}
