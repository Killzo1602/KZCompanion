import 'dart:async';
import 'dart:math' show Random;

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/connection/usb_route.dart';
import 'package:flutter/foundation.dart';
import 'package:signalr_netcore/signalr_client.dart';
import 'package:signalr_netcore/iretry_policy.dart';
import 'package:logging/logging.dart';

class _InfiniteRetryPolicy implements IRetryPolicy {
  @override
  int? nextRetryDelayInMilliseconds(RetryContext retryContext) {
    const steps = [0, 1000, 2000, 4000, 8000, 16000, 30000];
    final n = retryContext.previousRetryCount;
    return n < steps.length ? steps[n] : 30000;
  }
}

class SystemStats {
  final double cpuUsage;
  final double ramUsage;
  final double ramTotal;
  final double ramAvailable;
  final double netUp;
  final double netDown;
  final double gpuUsage;
  final List<DiskStat> disks;
  final double? cpuTemp;
  final double? cpuPower;
  final double? gpuTemp;
  final double? gpuHotspot;
  final double? gpuPower;
  final double? ssdTemp;
  final double? cpuClock;
  final double? gpuClock;
  final double? gpuMemUsed;
  final double? gpuMemTotal;
  final List<FanStat> fans;
  final List<DriveTemp> drives;
  final bool sensorsElevated;
  final bool cpuDriverMissing;
  final int cpuSensorCount;
  final String netAdapter;

  SystemStats({
    this.cpuUsage = 0,
    this.ramUsage = 0,
    this.ramTotal = 0,
    this.ramAvailable = 0,
    this.netUp = 0,
    this.netDown = 0,
    this.gpuUsage = 0,
    this.disks = const [],
    this.cpuTemp,
    this.cpuPower,
    this.gpuTemp,
    this.gpuHotspot,
    this.gpuPower,
    this.ssdTemp,
    this.cpuClock,
    this.gpuClock,
    this.gpuMemUsed,
    this.gpuMemTotal,
    this.fans = const [],
    this.drives = const [],
    this.sensorsElevated = false,
    this.cpuDriverMissing = false,
    this.cpuSensorCount = 0,
    this.netAdapter = '',
  });

  double get ramUsedPercent => ramUsage > 0
      ? ramUsage
      : (ramTotal > 0 ? (ramTotal - ramAvailable) / ramTotal * 100 : 0);
  double get ramUsedGb => (ramTotal - ramAvailable) / 1024.0;
  double get ramTotalGb => ramTotal / 1024.0;

  factory SystemStats.fromJson(Map<String, dynamic> json) {
    double parseDouble(dynamic val) {
      if (val == null) return 0;
      if (val is num) return val.toDouble();
      if (val is String) return double.tryParse(val) ?? 0;
      return 0;
    }

    double? opt(String k) {
      final v = json[k] ?? json[k[0].toUpperCase() + k.substring(1)];
      return v is num ? v.toDouble() : null;
    }

    return SystemStats(
      sensorsElevated: (json['sensorsElevated'] ?? json['SensorsElevated']) == true,
      cpuDriverMissing: (json['cpuDriverMissing'] ?? json['CpuDriverMissing']) == true,
      cpuSensorCount: ((json['cpuSensorCount'] ?? json['CpuSensorCount']) as num?)?.toInt() ?? 0,
      netAdapter: '${json['netAdapter'] ?? json['NetAdapter'] ?? ''}',
      drives: ((json['drives'] ?? json['Drives']) as List? ?? const [])
          .whereType<Map>()
          .map((d) => DriveTemp('${d['name'] ?? d['Name'] ?? ''}',
              ((d['temp'] ?? d['Temp']) as num?)?.toDouble() ?? 0))
          .toList(),
      cpuTemp: opt('cpuTemp'),
      cpuPower: opt('cpuPower'),
      gpuTemp: opt('gpuTemp'),
      gpuHotspot: opt('gpuHotspot'),
      gpuPower: opt('gpuPower'),
      ssdTemp: opt('ssdTemp'),
      cpuClock: opt('cpuClock'),
      gpuClock: opt('gpuClock'),
      gpuMemUsed: opt('gpuMemUsed'),
      gpuMemTotal: opt('gpuMemTotal'),
      fans: ((json['fans'] ?? json['Fans']) as List? ?? const [])
          .whereType<Map>()
          .map((f) => FanStat('${f['name'] ?? f['Name'] ?? ''}',
              ((f['rpm'] ?? f['Rpm']) as num?)?.toInt() ?? 0))
          .toList(),
      cpuUsage: parseDouble(json['cpuUsage'] ?? json['CpuUsage']),
      ramUsage: parseDouble(json['ramUsage'] ?? json['RamUsage']),
      ramTotal: parseDouble(json['ramTotal'] ?? json['RamTotal']),
      ramAvailable: parseDouble(json['ramAvailable'] ?? json['RamAvailable']),
      netUp: parseDouble(json['netUpKBps'] ?? json['NetUpKBps']),
      netDown: parseDouble(json['netDownKBps'] ?? json['NetDownKBps']),
      gpuUsage: parseDouble(json['gpuUsage'] ?? json['GpuUsage']),
      disks: ((json['disks'] ?? json['Disks']) as List? ?? const [])
          .whereType<Map>()
          .map((d) => DiskStat(
                name: '${d['name'] ?? d['Name'] ?? ''}',
                label: '${d['label'] ?? d['Label'] ?? ''}',
                totalBytes: parseDouble(d['totalBytes'] ?? d['TotalBytes']),
                freeBytes: parseDouble(d['freeBytes'] ?? d['FreeBytes']),
              ))
          .toList(),
    );
  }
}

class DriveTemp {
  final String name;
  final double temp;
  const DriveTemp(this.name, this.temp);
}

class FanStat {
  final String name;
  final int rpm;
  const FanStat(this.name, this.rpm);
}

class DiskStat {
  final String name;
  final String label;
  final double totalBytes;
  final double freeBytes;

  const DiskStat({required this.name, this.label = '', this.totalBytes = 0, this.freeBytes = 0});

  double get usedPercent => totalBytes > 0 ? (totalBytes - freeBytes) / totalBytes * 100 : 0;
}

class SignalRService {
  SignalRService() {
    effect(() {
      connectionStatus.value;
      untracked(_updateLink);
    });
    effect(() {
      AppLanguage.current.value;
      untracked(_sendLanguage);
    });
  }

  HubConnection? _hubConnection;
  final _logger = Logger('SignalRService');
  final systemStats = signal<SystemStats>(SystemStats());
  final connectionStatus = signal<String>("Disconnected");
  final lastDebugData = signal<String>("No Data");
  final currentUrl =
      signal<String>("Not Initialized");

  final volumeUpdates = signal<Map<String, dynamic>>({});
  final mediaStateUpdates = signal<Map<String, dynamic>?>(null);

  final deckUpdated = signal<int>(0);

  final linkState = signal<String>('reconnecting');
  final diagLog = signal<List<String>>([]);
  final pcName = signal<String>('');
  final pcOs = signal<String>('');
  DateTime _lastMessageAt = DateTime.now();
  DateTime? _failingSince;
  Timer? _watchdog;

  void diag(String message) {
    final t = DateTime.now();
    String two(int v) => v.toString().padLeft(2, '0');
    final line = '${two(t.hour)}:${two(t.minute)}:${two(t.second)} $message';
    final next = [...diagLog.value, line];
    diagLog.value = next.length > 60 ? next.sublist(next.length - 60) : next;
    _logger.info(message);
  }

  void _updateLink() {
    if (connectionStatus.value == 'Connected') {
      _failingSince = null;
      linkState.value = 'online';
      return;
    }
    _failingSince ??= DateTime.now();
    linkState.value = DateTime.now().difference(_failingSince!) > const Duration(seconds: 15)
        ? 'offline'
        : 'reconnecting';
  }

  void _startWatchdog() {
    _watchdog ??= Timer.periodic(const Duration(seconds: 5), (_) => _healthCheck());
  }

  void _healthCheck() {
    if (_stopped || _lastBaseUrl == null) return;
    final conn = _hubConnection;
    if (conn != null && conn.state == HubConnectionState.Connected) {
      final silent = DateTime.now().difference(_lastMessageAt);
      if (silent > const Duration(seconds: 12)) {
        diag('no data for ${silent.inSeconds}s: restarting the connection');
        _restart();
      }
    } else if (!(_retryTimer?.isActive ?? false) &&
        (conn == null || conn.state == HubConnectionState.Disconnected)) {
      _scheduleRetry();
    }
    _updateLink();
  }

  void onAppResumed() {
    diag('app resumed');
    final conn = _hubConnection;
    if (conn == null || conn.state != HubConnectionState.Connected) {
      _retryTimer?.cancel();
      _retryCount = 0;
      _restart();
      return;
    }
    Timer(const Duration(seconds: 3), () {
      if (_stopped || connectionStatus.value != 'Connected') return;
      if (DateTime.now().difference(_lastMessageAt) > const Duration(milliseconds: 3500)) {
        diag('no response after resume: restarting');
        _restart();
      }
    });
  }

  Future<void> _preferUsb() async {
    final url = _lastBaseUrl;
    if (_stopped || _probingUsb || url == null || url.isEmpty || UsbRoute.isUsb(url)) return;
    if (_hubConnection?.state != HubConnectionState.Connected) return;
    _probingUsb = true;
    try {
      final route = await UsbRoute.pick(url, apiKey: _lastApiKey);
      if (!_stopped && _lastBaseUrl == url && UsbRoute.isUsb(route)) {
        diag('USB available: switching');
        await _restart();
      }
    } finally {
      _probingUsb = false;
    }
  }

  Future<void> _restart() async {
    final url = _lastBaseUrl;
    if (url == null || url.isEmpty) return;
    _stopped = true;
    _retryTimer?.cancel();
    _retryTimer = null;
    final dead = _hubConnection;
    _hubConnection = null;
    try {
      await dead?.stop();
    } catch (_) {}
    _retryCount = 0;
    final route = await UsbRoute.pick(url, apiKey: _lastApiKey);
    if (route != url) Injector.get<DroidDeckClient>().setBaseUrl(route);
    await init(route, apiKey: _lastApiKey);
  }

  final audioPush = signal<Map<String, dynamic>?>(null);

  final appsChanged = signal<int>(0);

  final runningAppsPush = signal<List<Map<String, dynamic>>?>(null);
  bool _watchingRunningApps = false;

  final muteStates = signal<Map<String, bool>>({});

  final discordState =
      signal<Map<String, dynamic>>({'connected': false, 'mute': false, 'deaf': false});

  final deckLayout = signal<Map<String, int>>({'rows': 3, 'columns': 5});

  final mediaPlaying = signal<bool>(false);

  final obsState = signal<Map<String, dynamic>>({'connected': false});

  final tuyaState = signal<Map<String, dynamic>>({'paired': false, 'devices': []});

  final autoProfile = signal<Map<String, dynamic>>({});

  final actionStates = signal<Map<String, Map<String, dynamic>>>({});
  final Set<String> _stateEvents = {
    'ReceiveButtonToggles',
    'ReceiveInfoState',
    'ReceiveNowPlaying',
  };

  void listenState(String event) {
    if (!_stateEvents.add(event)) return;
    _registerStateHandler(event);
  }

  void _registerStateHandler(String event) {
    if (const {'ReceiveObsState', 'ReceiveDiscordState', 'ReceiveTuyaState'}
        .contains(event)) {
      return;
    }
    _hubConnection?.on(event, (arguments) {
      if (arguments == null || arguments.isEmpty) return;
      try {
        seedState(event, Map<String, dynamic>.from(arguments[0] as Map));
      } catch (e) {
        _logger.warning('Error parsing $event: $e');
      }
    });
  }

  void seedState(String event, Map<String, dynamic> state) {
    actionStates.value = {
      ...actionStates.value,
      event: {...state, '_receivedAt': DateTime.now().millisecondsSinceEpoch},
    };
  }

  Map<String, dynamic> stateOf(String event) {
    switch (event) {
      case 'ReceiveObsState':
        return obsState.value;
      case 'ReceiveDiscordState':
        return discordState.value;
      case 'ReceiveTuyaState':
        return tuyaState.value;
      default:
        return actionStates.value[event] ?? const {};
    }
  }

  String? _lastBaseUrl;
  String? _lastApiKey;

  final activeBaseUrl = signal<String>('');

  Timer? _usbTimer;
  bool _probingUsb = false;
  Timer? _retryTimer;
  int _retryCount = 0;

  bool _stopped = false;

  void _scheduleRetry() {
    if (_stopped) return;
    final url = _lastBaseUrl;
    if (url == null || url.isEmpty) return;
    if (_retryTimer?.isActive ?? false) return;

    const steps = [1000, 2000, 4000, 8000, 16000, 30000];
    final base = steps[_retryCount < steps.length ? _retryCount : steps.length - 1];
    final delay = (base * (0.9 + Random().nextDouble() * 0.2)).round();
    _retryCount++;
    diag('retry $_retryCount in ${(delay / 1000).toStringAsFixed(1)}s');
    connectionStatus.value = 'Reconnecting in ${delay ~/ 1000}s...';

    _retryTimer = Timer(Duration(milliseconds: delay), () async {
      final dead = _hubConnection;
      _hubConnection = null;
      try {
        await dead?.stop();
      } catch (_) {
      }
      final route = await UsbRoute.pick(url, apiKey: _lastApiKey);
      if (route != url) Injector.get<DroidDeckClient>().setBaseUrl(route);
      await init(route, apiKey: _lastApiKey);
    });
  }

  Future<void> dispose() async {
    _stopped = true;
    _retryTimer?.cancel();
    _retryTimer = null;
    _usbTimer?.cancel();
    _usbTimer = null;
    final conn = _hubConnection;
    _hubConnection = null;
    try {
      await conn?.stop();
    } catch (_) {}
  }

  static String _redactUrl(String url) =>
      url.replaceAll(RegExp(r'access_token=[^&]+'), 'access_token=***');

  Future<void> init(String baseUrl, {String? apiKey}) async {
    if (_hubConnection != null) return;

    _lastBaseUrl = baseUrl;
    _lastApiKey = apiKey;
    _stopped = false;
    activeBaseUrl.value = baseUrl;
    _usbTimer ??= Timer.periodic(const Duration(seconds: 10), (_) => _preferUsb());

    String hubUrl = baseUrl;
    if (hubUrl.isEmpty) {
      if (kIsWeb) {
        hubUrl = '${Uri.base.origin}/deckHub';
      } else {
        _logger.warning('SignalR init skipped: Base URL is empty on mobile');
        connectionStatus.value = "URL Empty";
        currentUrl.value = "Empty (init called with empty baseUrl)";
        return;
      }
    } else {
      hubUrl = '$baseUrl/deckHub';
    }

    hubUrl += '${hubUrl.contains('?') ? '&' : '?'}lang=${AppLanguage.current.value}';

    if (apiKey != null && apiKey.isNotEmpty) {
      final sep = hubUrl.contains('?') ? '&' : '?';
      hubUrl += '${sep}access_token=${Uri.encodeQueryComponent(apiKey)}';
    }

    currentUrl.value = _redactUrl(hubUrl);
    _logger.info('Connecting to SignalR Hub at ${_redactUrl(hubUrl)}');
    connectionStatus.value = "Connecting...";
    diag('connecting to ${_redactUrl(hubUrl)}');
    _lastMessageAt = DateTime.now();
    _startWatchdog();

    try {
      _hubConnection = HubConnectionBuilder()
          .withUrl(hubUrl)
          .withAutomaticReconnect(reconnectPolicy: _InfiniteRetryPolicy())
          .build();
      _hubConnection!.keepAliveIntervalInMilliseconds = 10000;
      _hubConnection!.serverTimeoutInMilliseconds = 60000;
    } catch (e) {
      _logger.severe('Error building HubConnection: $e');
      connectionStatus.value = "Build Error";
      return;
    }

    _hubConnection?.on('ReceiveAudioState', (arguments) {
      if (arguments == null || arguments.isEmpty) return;
      try {
        audioPush.value = Map<String, dynamic>.from(arguments[0] as Map);
      } catch (e) {
        _logger.warning('Error parsing ReceiveAudioState: $e');
      }
    });

    _hubConnection?.on('ReceiveRunningApps', (arguments) {
      if (arguments == null || arguments.isEmpty) return;
      try {
        final list = (arguments[0] as Map)['apps'] as List? ?? const [];
        runningAppsPush.value = list.map((e) => (e as Map).cast<String, dynamic>()).toList();
      } catch (e) {
        _logger.warning('Error parsing ReceiveRunningApps: $e');
      }
    });

    _hubConnection?.on('ReceiveAppsChanged', (_) {
      appsChanged.value = appsChanged.value + 1;
    });

    _hubConnection?.on('ReceiveSystemStats', (arguments) {
      _lastMessageAt = DateTime.now();
      if (arguments != null && arguments.isNotEmpty) {
        try {
          final raw = arguments[0].toString();
          lastDebugData.value = "Data: $raw";

          final data = arguments[0] as Map<String, dynamic>;
          systemStats.value = SystemStats.fromJson(data);
        } catch (e) {
          _logger.warning('Error parsing stats: $e');
          lastDebugData.value = "Parse Error: $e";
        }
      } else {
        lastDebugData.value = "Empty Args";
      }
    });

    _hubConnection?.on('ReceiveVolumeChange', (arguments) {
      if (arguments != null && arguments.isNotEmpty) {
        try {
          final update = arguments[0] as Map<String, dynamic>;
          final deviceId = update['deviceId'] as String;
          final type = update['type'] as String;
          final data = update['data'] as Map<String, dynamic>;

          final current = Map<String, dynamic>.from(volumeUpdates.value);
          current[deviceId] = {
            'type': type,
            'data': data,
            'timestamp': DateTime.now().millisecondsSinceEpoch,
          };
          volumeUpdates.value = current;

          _logger.info('Volume update: $deviceId ($type)');
        } catch (e) {
          _logger.warning('Error parsing ReceiveVolumeChange: $e');
        }
      }
    });

    _hubConnection?.on('ReceiveMediaState', (arguments) {
      if (arguments != null && arguments.isNotEmpty) {
        try {
          final update = arguments[0] as Map<String, dynamic>;
          mediaStateUpdates.value = update;
          _logger.info('Media update: ${update['sessionId']}');
        } catch (e) {
          _logger.warning('Error parsing ReceiveMediaState: $e');
        }
      }
    });

    _hubConnection?.on('ReceiveDeckUpdate', (arguments) {
      deckUpdated.value = deckUpdated.value + 1;
      _logger.info('Deck update recebido');
    });

    _hubConnection?.on('ReceiveMuteState', (arguments) {
      if (arguments != null && arguments.isNotEmpty) {
        try {
          final u = arguments[0] as Map<String, dynamic>;
          final pn = u['processName'] as String?;
          final muted = u['muted'];
          if (pn != null && muted is bool) {
            final cur = Map<String, bool>.from(muteStates.value);
            cur[pn] = muted;
            muteStates.value = cur;
          }
        } catch (e) {
          _logger.warning('Error parsing ReceiveMuteState: $e');
        }
      }
    });

    _hubConnection?.on('ReceiveDiscordState', (arguments) {
      if (arguments != null && arguments.isNotEmpty) {
        try {
          discordState.value =
              Map<String, dynamic>.from(arguments[0] as Map);
        } catch (e) {
          _logger.warning('Error parsing ReceiveDiscordState: $e');
        }
      }
    });

    _hubConnection?.on('ReceiveLayoutUpdate', (arguments) {
      if (arguments != null && arguments.isNotEmpty) {
        try {
          final m = arguments[0] as Map;
          deckLayout.value = {
            'rows': (m['rows'] as num).toInt(),
            'columns': (m['columns'] as num).toInt(),
          };
        } catch (e) {
          _logger.warning('Error parsing ReceiveLayoutUpdate: $e');
        }
      }
    });

    _hubConnection?.on('ReceiveMediaStatus', (arguments) {
      if (arguments != null && arguments.isNotEmpty) {
        try {
          final m = arguments[0] as Map;
          mediaPlaying.value = m['playing'] == true;
        } catch (e) {
          _logger.warning('Error parsing ReceiveMediaStatus: $e');
        }
      }
    });

    _hubConnection?.on('ReceiveObsState', (arguments) {
      if (arguments != null && arguments.isNotEmpty) {
        try {
          obsState.value = Map<String, dynamic>.from(arguments[0] as Map);
        } catch (e) {
          _logger.warning('Error parsing ReceiveObsState: $e');
        }
      }
    });

    _hubConnection?.on('ReceiveTuyaState', (arguments) {
      if (arguments != null && arguments.isNotEmpty) {
        try {
          tuyaState.value = Map<String, dynamic>.from(arguments[0] as Map);
        } catch (e) {
          _logger.warning('Error parsing ReceiveTuyaState: $e');
        }
      }
    });

    _hubConnection?.on('ReceiveTuyaDeviceState', (arguments) {
      if (arguments == null || arguments.isEmpty) return;
      try {
        final msg = Map<String, dynamic>.from(arguments[0] as Map);
        final deviceId = msg['deviceId'];
        final changes = Map<String, dynamic>.from(msg['status'] as Map);

        final snapshot = Map<String, dynamic>.from(tuyaState.value);
        final devices = List<dynamic>.from(snapshot['devices'] as List? ?? const []);

        final index = devices.indexWhere((d) => (d as Map)['id'] == deviceId);
        if (index < 0) return;

        final device = Map<String, dynamic>.from(devices[index] as Map);
        device['status'] = {
          ...Map<String, dynamic>.from(device['status'] as Map? ?? const {}),
          ...changes,
        };
        devices[index] = device;

        snapshot['devices'] = devices;
        tuyaState.value = snapshot;
      } catch (e) {
        _logger.warning('Error parsing ReceiveTuyaDeviceState: $e');
      }
    });

    for (final event in _stateEvents) {
      _registerStateHandler(event);
    }

    _hubConnection?.on('ReceiveAutoProfile', (arguments) {
      if (arguments == null || arguments.isEmpty) return;
      try {
        autoProfile.value = Map<String, dynamic>.from(arguments[0] as Map);
      } catch (e) {
        _logger.warning('Error parsing ReceiveAutoProfile: $e');
      }
    });

    _hubConnection?.onclose(({error}) {
      connectionStatus.value = "Disconnected: $error";
      diag('connection closed${error == null ? '' : ': $error'}');
      _scheduleRetry();
    });

    _hubConnection?.onreconnecting(({error}) {
      connectionStatus.value = "Reconnecting...";
      diag('reconnecting${error == null ? '' : ': $error'}');
    });

    _hubConnection?.onreconnected(({connectionId}) {
      connectionStatus.value = "Connected";
      _lastMessageAt = DateTime.now();
      diag('reconnected');
      _retryCount = 0;
      _sendLanguage();
      _resubscribeRunningApps();
    });

    try {
      await _hubConnection?.start();
      _logger.info('SignalR Connected');
      connectionStatus.value = "Connected";
      _lastMessageAt = DateTime.now();
      diag('connected');
      _retryCount = 0;
      _retryTimer?.cancel();
      _sendLanguage();
      _resubscribeRunningApps();
    } catch (e) {
      _logger.severe('SignalR Connection Failed: $e');
      connectionStatus.value = "Conn Error: $e";
      diag('connect failed: $e');
      _scheduleRetry();
    }
  }

  Future<void> watchRunningApps(bool watch) async {
    _watchingRunningApps = watch;
    final conn = _hubConnection;
    if (conn == null || conn.state != HubConnectionState.Connected) return;
    try {
      await conn.invoke(watch ? 'WatchRunningApps' : 'UnwatchRunningApps');
    } catch (e) {
      _logger.warning('Error invoking WatchRunningApps: $e');
    }
  }

  void _resubscribeRunningApps() {
    if (_watchingRunningApps) watchRunningApps(true);
  }

  Future<void> _sendLanguage() async {
    final conn = _hubConnection;
    if (conn == null || conn.state != HubConnectionState.Connected) return;
    try {
      await conn.invoke('SetLanguage', args: [AppLanguage.current.value]);
    } catch (_) {
    }
  }

  bool holdKey(String buttonId) => _invokeKey("HoldKey", buttonId);

  bool releaseKey(String buttonId) => _invokeKey("ReleaseKey", buttonId);

  void companionKey(int key, bool pressed) {
    final conn = _hubConnection;
    if (conn == null || conn.state != HubConnectionState.Connected) return;
    conn.invoke("CompanionKey", args: [key, pressed]).catchError((e) {
      _logger.warning("Error invoking CompanionKey: $e");
      return null;
    });
  }

  bool _invokeKey(String method, String buttonId) {
    final conn = _hubConnection;
    if (conn == null || conn.state != HubConnectionState.Connected) return false;
    conn.invoke(method, args: [buttonId]).catchError((e) {
      _logger.warning("Error invoking $method: $e");
      return null;
    });
    return true;
  }

  Future<void> updateUrl(String baseUrl, {String? apiKey}) async {
    _stopped = true;
    _retryTimer?.cancel();
    _retryTimer = null;
    _retryCount = 0;

    if (_hubConnection != null) {
      final conn = _hubConnection!;
      _hubConnection = null;
      try {
        await conn.stop();
      } catch (_) {}
    }
    await init(baseUrl, apiKey: apiKey);
  }
}
