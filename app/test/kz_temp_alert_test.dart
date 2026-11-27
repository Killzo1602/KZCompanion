import 'package:droiddeck/src/kz/kz_temp_alert.dart';
import 'package:droiddeck/src/kz/kz_widgets.dart';
import 'package:droiddeck/src/services/signalr_service.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('warns only after 15 s hot, respects dismiss and rearms after cooling', () {
    var now = DateTime.utc(2026);
    final alert = KzTempAlert(SignalRService(), clock: () => now);
    void tick(int seconds, double? cpu, [double? gpu]) {
      now = now.add(Duration(seconds: seconds));
      alert.update(cpu, gpu);
    }

    tick(0, 90);
    tick(14, 90);
    expect(alert.message.value, isNull);
    tick(1, 91, 60);
    expect(alert.message.value, 'High temperature: CPU 91°C');

    alert.dismiss();
    tick(30, 92);
    expect(alert.message.value, isNull);

    tick(1, 50);
    tick(1, 90);
    tick(15, 90);
    expect(alert.message.value, 'High temperature: CPU 90°C');

    tick(1, null);
    expect(alert.message.value, isNull);
    alert.dispose();
  });

  test('clock and video memory text', () {
    expect(fmtClock(null), 'Unavailable');
    expect(fmtClock(4725), '4.72 GHz');
    expect(fmtVram(3072, 12288), '3.0 / 12 GB');
    expect(fmtVram(512, null), '0.5 GB');
    expect(fmtVram(null, 8192), 'Unavailable');
  });
}
