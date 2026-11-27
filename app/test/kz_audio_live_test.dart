import 'dart:io';

import 'package:droiddeck/core/core.dart';
import 'package:droiddeck/src/kz/kz_audio.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final url = Platform.environment['KZ_LIVE_URL'];
  final key = Platform.environment['KZ_LIVE_KEY'];

  test('Android client code -> real backend -> state', () async {
    HttpOverrides.global = null;
    final client = DroidDeckClient(baseUrl: url!);
    client.setApiKey(key);
    final signalR = SignalRService();
    signalR.connectionStatus.value = 'Connected';
    expect(signalR.linkState.value, 'online');

    final store = AudioStore(client: client, signalR: signalR);
    await store.refresh();
    final s0 = store.state.value!;
    // ignore: avoid_print
    print('real state: master=${s0.volume} out="${s0.outputName}" muted=${s0.muted} '
        'mic=${s0.inputMuted} sessions=${s0.sessions.map((x) => '${x.name}:${x.volume}${x.muted ? 'M' : ''}').toList()}');
    expect(store.supported.value, true);
    expect(s0.volume, isNotNull);
    expect(s0.sessions, isNotEmpty);
    expect(store.icons.value, isNotEmpty);

    final v0 = s0.volume!;
    store.setMasterVolume(v0 - 1, commit: true);
    await Future<void>.delayed(const Duration(milliseconds: 600));
    var real = AudioState.fromJson(await client.getAudio());
    expect(real.volume, v0 - 1, reason: 'Windows deveria ter ido para ${v0 - 1}');
    store.setMasterVolume(v0, commit: true);
    await Future<void>.delayed(const Duration(milliseconds: 600));
    real = AudioState.fromJson(await client.getAudio());
    expect(real.volume, v0);

    final sys = s0.sessions.firstWhere((x) => x.system);
    store.setSessionVolume(sys.id, 80, commit: true);
    await Future<void>.delayed(const Duration(milliseconds: 600));
    real = AudioState.fromJson(await client.getAudio());
    expect(real.sessions.firstWhere((x) => x.id == sys.id).volume, 80);
    for (final o in s0.sessions.where((x) => x.id != sys.id)) {
      expect(real.sessions.firstWhere((x) => x.id == o.id).volume, o.volume, reason: '${o.name} nao pode mudar');
    }
    store.toggleSessionMute(sys.id);
    await Future<void>.delayed(const Duration(milliseconds: 600));
    expect(AudioState.fromJson(await client.getAudio()).sessions.firstWhere((x) => x.id == sys.id).muted, true);
    store.toggleSessionMute(sys.id);
    store.setSessionVolume(sys.id, sys.volume, commit: true);
    await Future<void>.delayed(const Duration(milliseconds: 600));

    store.toggleMasterMute();
    await Future<void>.delayed(const Duration(milliseconds: 600));
    expect(AudioState.fromJson(await client.getAudio()).muted, !s0.muted);
    store.toggleMasterMute();
    await Future<void>.delayed(const Duration(milliseconds: 600));
    expect(AudioState.fromJson(await client.getAudio()).muted, s0.muted);

    final out0 = s0.muted;
    final mic0 = s0.inputMuted!;
    store.toggleMicMute();
    await Future<void>.delayed(const Duration(milliseconds: 700));
    expect(store.state.value!.inputMuted, !mic0, reason: 'botao Mute deve mudar o microfone');
    expect(store.state.value!.muted, out0, reason: 'botao Mute nao pode mudar os alto-falantes');
    store.toggleMicMute();
    await Future<void>.delayed(const Duration(milliseconds: 700));
    expect(store.state.value!.inputMuted, mic0);
    store.toggleDeafen();
    await Future<void>.delayed(const Duration(milliseconds: 700));
    expect(store.state.value!.deafened, true);
    expect(store.state.value!.muted, true);
    expect(store.state.value!.inputMuted, true);
    store.toggleDeafen();
    await Future<void>.delayed(const Duration(milliseconds: 700));
    expect(store.state.value!.deafened, false);
    expect(store.state.value!.muted, out0);
    expect(store.state.value!.inputMuted, mic0);

    signalR.audioPush.value = {
      'outputName': s0.outputName,
      'volume': 33,
      'muted': false,
      'inputMuted': s0.inputMuted,
      'deafened': false,
      'sessions': [
        for (final x in s0.sessions)
          {'id': x.id, 'name': x.name, 'volume': x.volume, 'muted': x.muted, 'active': x.active, 'system': x.system}
      ],
    };
    await Future<void>.delayed(const Duration(milliseconds: 1200));
    signalR.audioPush.value = {
      'outputName': s0.outputName,
      'volume': 34,
      'muted': false,
      'inputMuted': s0.inputMuted,
      'deafened': false,
      'sessions': const [],
    };
    expect(store.state.value!.volume, 34);

    store.setSessionVolume('deadbeef0000', 10, commit: true);
    await Future<void>.delayed(const Duration(milliseconds: 600));

    final bad = DroidDeckClient(baseUrl: url);
    bad.setApiKey('wrong');
    final badStore = AudioStore(client: bad, signalR: signalR);
    await badStore.refresh();
    expect(badStore.lastError.value, isNotNull);
    // ignore: avoid_print
    print('bad key message: ${badStore.lastError.value}');
  }, skip: url == null ? 'defina KZ_LIVE_URL' : false);
}
