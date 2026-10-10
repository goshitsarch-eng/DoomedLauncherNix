import 'dart:async';
import 'package:doomedlauncher/bridge/backend.dart';
import 'package:doomedlauncher/state/app_controller.dart';
import 'package:doomedlauncher/state/diagnostics.dart';
import 'package:flutter_test/flutter_test.dart';

class ControlledBackend extends Backend {
  ControlledBackend() : super(diagnostics: Diagnostics(fallback: (_) {}));
  Future<void> Function()? onInitialize;
  late FutureOr<Map<String, dynamic>> Function(String op) onCall;
  @override
  Future<void> initialize() async {
    await onInitialize?.call();
  }

  @override
  Future<Map<String, dynamic>> call(
    String op, [
    Map<String, dynamic> args = const {},
  ]) async => onCall(op);
}

void main() {
  test(
    'shutdown blocks new work synchronously until quitting is cancelled',
    () async {
      final backend = ControlledBackend();
      final calls = <String>[];
      backend.onCall = (op) {
        calls.add(op);
        return <String, dynamic>{};
      };
      final controller = AppController(backend);
      expect(controller.beginShutdown(), isTrue);
      expect(controller.shutdownInProgress, isTrue);
      expect(controller.busy, isTrue);
      expect(controller.beginShutdown(), isFalse);
      for (final operation in ['launch', 'import_files', 'start_download']) {
        expect(await controller.execute(operation), isNull);
      }
      await controller.initialize();
      expect(calls, isEmpty);
      expect(controller.error, contains('launcher is closing'));
      expect(controller.shutdownInProgress, isTrue);

      controller.cancelShutdown();
      expect(controller.shutdownInProgress, isFalse);
      expect(controller.busy, isFalse);
      expect(await controller.execute('launch'), isNotNull);
      expect(calls, ['launch', 'snapshot']);
      expect(controller.error, isNull);
      controller.dispose();
    },
  );

  test(
    'pending job admission settles before shutdown may inspect active jobs',
    () async {
      final backend = ControlledBackend();
      final started = Completer<Map<String, dynamic>>();
      final snapshot = Completer<Map<String, dynamic>>();
      backend.onCall = (op) =>
          op == 'start_download' ? started.future : snapshot.future;
      final controller = AppController(backend);
      final pending = controller.execute('start_download');
      expect(controller.busy, isTrue);
      expect(controller.beginShutdown(), isFalse);
      expect(controller.shutdownInProgress, isFalse);
      started.complete({
        'download': {'id': 'registered'},
      });
      await Future<void>.delayed(Duration.zero);
      expect(controller.beginShutdown(), isFalse);
      snapshot.complete({});
      expect(await pending, {
        'download': {'id': 'registered'},
      });
      expect(controller.beginShutdown(), isTrue);
      expect(controller.busy, isTrue);
      controller.dispose();
    },
  );

  test(
    'disposed controller cannot admit work or notify shutdown changes',
    () async {
      final backend = ControlledBackend();
      var calls = 0;
      backend.onCall = (_) {
        calls++;
        return {};
      };
      final controller = AppController(backend);
      var notifications = 0;
      controller.addListener(() => notifications++);
      controller.dispose();
      expect(controller.beginShutdown(), isFalse);
      controller.cancelShutdown();
      expect(await controller.execute('launch'), isNull);
      await controller.initialize();
      expect(calls, 0);
      expect(notifications, 0);
    },
  );

  test(
    'an older snapshot cannot overwrite the refresh after a successful mutation',
    () async {
      final backend = ControlledBackend();
      final oldSnapshot = Completer<Map<String, dynamic>>();
      var snapshots = 0;
      backend.onCall = (op) {
        if (op == 'save_tag') return <String, dynamic>{};
        if (++snapshots == 1) return oldSnapshot.future;
        return {
          'tags': [
            {'tag_id': 12, 'name': 'New tag', 'has_tab': true},
          ],
        };
      };
      final controller = AppController(backend);
      final oldRefresh = controller.refresh();
      expect(
        await controller.execute('save_tag', {
          'name': 'New tag',
          'has_tab': true,
        }),
        isNotNull,
      );
      expect(controller.tags.single['name'], 'New tag');
      oldSnapshot.complete({'tags': []});
      await oldRefresh;
      expect(controller.tags.single['name'], 'New tag');
      expect(controller.busy, isFalse);
      controller.dispose();
    },
  );

  test(
    'a superseded failed refresh cannot replace a newer successful state with an error',
    () async {
      final backend = ControlledBackend();
      final oldSnapshot = Completer<Map<String, dynamic>>();
      var snapshots = 0;
      backend.onCall = (op) =>
          ++snapshots == 1 ? oldSnapshot.future : {'version': 'new snapshot'};
      final controller = AppController(backend);
      final first = controller.refresh();
      await controller.refresh();
      oldSnapshot.completeError(
        const BackendException('network', 'Old request failed'),
      );
      await expectLater(first, completes);
      expect(controller.snapshot['version'], 'new snapshot');
      expect(controller.error, isNull);
      controller.dispose();
    },
  );

  test('current refresh failures remain visible to their caller', () async {
    final backend = ControlledBackend();
    backend.onCall = (_) =>
        throw const BackendException('io', 'Current request failed');
    final controller = AppController(backend);
    await expectLater(controller.refresh(), throwsA(isA<BackendException>()));
    controller.dispose();
  });

  test(
    'completion after disposal changes neither snapshot nor listener notifications',
    () async {
      final backend = ControlledBackend();
      final result = Completer<Map<String, dynamic>>();
      var calls = 0;
      backend.onCall = (_) {
        calls++;
        return result.future;
      };
      final controller = AppController(backend)
        ..snapshot = {'version': 'original'};
      var notifications = 0;
      controller.addListener(() => notifications++);
      final pending = controller.refresh();
      controller.dispose();
      result.complete({'version': 'late'});
      await pending;
      await controller.refresh();
      expect(controller.snapshot['version'], 'original');
      expect(notifications, 0);
      expect(calls, 1);
    },
  );

  test(
    'initialization retry clears a prior error and restores a working snapshot',
    () async {
      final backend = ControlledBackend();
      var attempts = 0;
      backend.onInitialize = () async {
        if (++attempts == 1) {
          throw const BackendException('native_library', 'Library missing');
        }
      };
      backend.onCall = (_) => {'version': 'ready'};
      final controller = AppController(backend);
      await controller.initialize();
      expect(controller.error, 'Library missing');
      expect(controller.busy, isFalse);
      await controller.initialize();
      expect(controller.error, isNull);
      expect(controller.snapshot['version'], 'ready');
      expect(controller.busy, isFalse);
      controller.dispose();
    },
  );
}
