import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/services/signalr_service.dart';

class HomeController {
  HomeController({DroidDeckClient? client, SignalRService? signalR})
      : _client = client ?? Injector.get<DroidDeckClient>(),
        _signalR = signalR ?? Injector.get<SignalRService>() {
    _init();
  }

  final DroidDeckClient _client;
  final SignalRService _signalR;

  late final outputs = signal<List<MixerEntity>>([]);
  late final inputs = signal<List<MixerEntity>>([]);
  late final isLoading = signal(false);
  late final error = signal<String?>(null);
  late final isFullscreen = signal(false);

  Timer? _pollingTimer;
  Timer? _signalRRefreshDebounce;
  Timer? _mediaRefreshDebounce;
  void Function()? _volumeSubDispose;
  void Function()? _mediaSubDispose;

  void _init() {
    _mediaSubDispose = _signalR.mediaStateUpdates.subscribe((update) {
      if (update == null) return;
      _mediaRefreshDebounce?.cancel();
      _mediaRefreshDebounce =
          Timer(const Duration(milliseconds: 400), fetchMediaSessions);
    });

    _volumeSubDispose = _signalR.volumeUpdates.subscribe((updates) {
      if (updates.isEmpty) return;

      _signalRRefreshDebounce?.cancel();
      _signalRRefreshDebounce = Timer(const Duration(milliseconds: 500), () {
        fetchOutputs();
        fetchInputs();
      });
    });
  }

  void startPolling() {
    fetchOutputs();
    fetchInputs();
    fetchMediaSessions();

    _pollingTimer = Timer.periodic(const Duration(seconds: 60), (_) {
      fetchOutputs();
      fetchInputs();
      fetchMediaSessions();
    });
  }

  void stopPolling() {
    _pollingTimer?.cancel();
    _pollingTimer = null;
  }

  Future<void> fetchOutputs() async {
    final isInitialLoad = outputs.value.isEmpty;
    if (isInitialLoad) {
      isLoading.value = true;
    }

    try {
      final res = await _client.getOutputs();
      outputs.value = res;
      error.value = null;
    } catch (e) {
      error.value = e.toString();
      debugPrint('DEBUG ERROR (outputs): $e');
    } finally {
      if (isInitialLoad) {
        isLoading.value = false;
      }
    }
  }

  Future<void> fetchInputs() async {
    final isInitialLoad = inputs.value.isEmpty;
    if (isInitialLoad) {
      isLoading.value = true;
    }

    try {
      debugPrint('DEBUG: Fetching inputs...');
      final res = await _client.getInputs();
      debugPrint('DEBUG: Got ${res.length} input devices');
      inputs.value = res;
      error.value = null;

      if (res.isNotEmpty) {
        debugPrint('DEBUG: First input device title: ${res.first.device.title}');
      } else {
        debugPrint('DEBUG: No input devices found');
      }
    } catch (e) {
      error.value = e.toString();
      debugPrint('DEBUG ERROR (inputs): $e');
    } finally {
      if (isInitialLoad) {
        isLoading.value = false;
      }
    }
  }

  Future<void> setVolume(String id, int volume) async {
    try {
      await _client.setOutput(id, MixerData(volume: volume));
      fetchOutputs();
    } catch (e) {
      error.value = e.toString();
    }
  }

  Future<void> toggleMute(String id, bool currentMute) async {
    try {
      await _client.setOutput(id, MixerData(mute: !currentMute));
      fetchOutputs();
    } catch (e) {
      error.value = e.toString();
    }
  }

  Future<void> setChannelVolume(
      String deviceId, int sessionId, int volume) async {
    try {
      await _client.setOutput(
          deviceId, MixerData(session: sessionId, volume: volume));
      fetchOutputs();
    } catch (e) {
      error.value = e.toString();
    }
  }

  Future<void> toggleChannelMute(
      String deviceId, int sessionId, bool currentMute) async {
    try {
      await _client.setOutput(
          deviceId, MixerData(session: sessionId, mute: !currentMute));
      fetchOutputs();
    } catch (e) {
      error.value = e.toString();
    }
  }

  void dispose() {
    stopPolling();
    _signalRRefreshDebounce?.cancel();
    _mediaRefreshDebounce?.cancel();
    _volumeSubDispose?.call();
    _volumeSubDispose = null;
    _mediaSubDispose?.call();
    _mediaSubDispose = null;
  }

  void toggleFullscreen() {
    isFullscreen.value = !isFullscreen.value;
  }

  late final mediaSessions = signal<Map<String, MediaSession>>({});

  Future<void> fetchMediaSessions() async {
    try {
      final sessions = await _client.getMediaSessions();
      debugPrint('DEBUG: Fetched ${sessions.length} media sessions');
      final sessionMap = <String, MediaSession>{};
      for (final session in sessions) {
        if (session.id != null) {
          sessionMap[session.id!] = session;
          debugPrint(
              'DEBUG: Media session ID: ${session.id} - Title: ${session.title}');
        }
      }
      mediaSessions.value = sessionMap;
    } catch (e) {
      debugPrint('DEBUG ERROR (media sessions): $e');
    }
  }

  Future<void> sendMediaCommand(String sessionId, String command) async {
    try {
      final result = await _client.sendMediaCommand(sessionId, command);

      final playing = result['playing'];
      final applied = result['sessionId'] as String? ?? sessionId;

      if (playing is bool) {
        final known = mediaSessions.value[applied];
        if (known != null) {
          mediaSessions.value = {
            ...mediaSessions.value,
            applied: known.copyWith(
                playbackStatus: playing ? 'Playing' : 'Paused'),
          };
        }
      }

      fetchMediaSessions();
    } catch (e) {
      debugPrint('DEBUG ERROR (send media command): $e');
    }
  }
}
