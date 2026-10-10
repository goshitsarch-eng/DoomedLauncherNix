import 'dart:convert';
import 'dart:io';
import 'package:doomedlauncher/bridge/backend.dart';
import 'package:doomedlauncher/state/app_controller.dart';
import 'package:doomedlauncher/state/diagnostics.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;

class FailingDiagnosticBackend extends Backend {
  FailingDiagnosticBackend(Diagnostics diagnostics)
    : super(diagnostics: diagnostics);
  @override
  Future<void> initialize() async => throw const BackendException(
    'permission',
    'Private path /private/person/library',
  );
  @override
  Future<Map<String, dynamic>> call(
    String op, [
    Map<String, dynamic> args = const {},
  ]) async {
    throw const BackendException(
      'network',
      'https://username:private-password@example.invalid/?token=private-token',
    );
  }
}

Future<List<Map<String, dynamic>>> readEvents(Directory root) async {
  final text = await File(
    p.join(root.path, 'Logs', 'application.jsonl'),
  ).readAsString();
  return const LineSplitter()
      .convert(text)
      .map((line) => jsonDecode(line) as Map<String, dynamic>)
      .toList();
}

void main() {
  late Directory directory;
  setUp(() async {
    directory = await Directory.systemTemp.createTemp('doomed-diagnostics-');
  });
  tearDown(() async {
    if (await directory.exists()) await directory.delete(recursive: true);
  });

  test(
    'concurrent diagnostic records rotate within a fixed file and byte budget',
    () async {
      final log = Diagnostics(
        dataRoot: directory.path,
        maxBytes: 512,
        maxFiles: 3,
      );
      await Future.wait(
        List.generate(
          40,
          (index) => log.record(
            source: DiagnosticSource.bridge,
            event: DiagnosticEvent.completed,
            operation: 'snapshot',
            durationMs: index,
          ),
        ),
      );
      final files = await Directory(
        p.join(directory.path, 'Logs'),
      ).list().where((entity) => entity is File).cast<File>().toList();
      expect(files, hasLength(3));
      final events = <Map<String, dynamic>>[];
      for (final file in files) {
        expect(await file.length(), lessThanOrEqualTo(512));
        for (final line in await file.readAsLines()) {
          events.add(jsonDecode(line) as Map<String, dynamic>);
        }
      }
      expect(
        events.map((event) => event['duration_ms']).toSet(),
        hasLength(events.length),
      );
      expect(events.map((event) => event['duration_ms']), contains(39));
      expect(events.map((event) => event['duration_ms']), isNot(contains(0)));
      expect((await readEvents(directory)).last['duration_ms'], 39);
    },
  );

  test(
    'error persistence retains safe codes while UI retains its full message',
    () async {
      final log = Diagnostics(dataRoot: directory.path);
      final controller = AppController(FailingDiagnosticBackend(log));
      expect(
        await controller.execute('search_idgames', {'query': 'private-search'}),
        isNull,
      );
      expect(controller.error, contains('private-password'));
      await log.flush();
      final event = (await readEvents(directory)).single;
      expect(event['source'], 'flutter');
      expect(event['event'], 'failed');
      expect(event['operation'], 'search_idgames');
      expect(event['code'], 'network');
      expect(event['duration_ms'], isNonNegative);
      expect(
        event.keys,
        unorderedEquals([
          'time',
          'source',
          'event',
          'operation',
          'code',
          'duration_ms',
        ]),
      );
      final raw = await File(
        p.join(directory.path, 'Logs', 'application.jsonl'),
      ).readAsString();
      for (final private in [
        'username',
        'private-password',
        'private-token',
        'private-search',
        'https://',
      ]) {
        expect(raw, isNot(contains(private)));
      }
      controller.dispose();
    },
  );

  test(
    'unknown operation and code strings are never copied into diagnostic output',
    () async {
      final log = Diagnostics(dataRoot: directory.path);
      await log.record(
        source: DiagnosticSource.platform,
        event: DiagnosticEvent.failed,
        operation: 'secret_password',
        code: 'token=do-not-persist',
        durationMs: -5,
      );
      final event = (await readEvents(directory)).single;
      expect(event['operation'], 'unknown');
      expect(event['code'], 'unknown');
      expect(event['duration_ms'], 0);
      expect(event.toString(), isNot(contains('secret_password')));
      expect(event.toString(), isNot(contains('do-not-persist')));
    },
  );

  test(
    'initialized controller failures and reported errors share one diagnostic sink',
    () async {
      final log = Diagnostics(dataRoot: directory.path);
      final controller = AppController(FailingDiagnosticBackend(log));
      await controller.initialize();
      expect(controller.busy, isFalse);
      expect(controller.error, contains('Private path'));
      controller.reportError(
        const BackendException('validation', 'Private screenshot filename'),
      );
      await log.flush();
      final events = await readEvents(directory);
      expect(events.map((event) => event['operation']), [
        'initialize',
        'report_error',
      ]);
      expect(events.map((event) => event['code']), [
        'permission',
        'validation',
      ]);
      expect(events.toString(), isNot(contains('Private')));
      controller.dispose();
    },
  );

  test(
    'sink failure falls back without exposing paths or breaking later logging',
    () async {
      final blocked = File(p.join(directory.path, 'not-a-directory'));
      await blocked.writeAsString('keep me');
      final fallback = <String>[];
      final log = Diagnostics(dataRoot: blocked.path, fallback: fallback.add);
      await log.record(
        source: DiagnosticSource.flutter,
        event: DiagnosticEvent.failed,
        operation: 'initialize',
        code: 'permission',
      );
      expect(fallback, hasLength(1));
      expect(fallback.single, contains('local log unavailable'));
      expect(fallback.single, isNot(contains(blocked.path)));
      expect(await blocked.readAsString(), 'keep me');
      log.configure(directory.path);
      await log.record(
        source: DiagnosticSource.flutter,
        event: DiagnosticEvent.completed,
        operation: 'initialize',
      );
      expect((await readEvents(directory)).single['event'], 'completed');
    },
  );

  test(
    'unconfigured or failed fallback sinks never throw into the application',
    () async {
      final messages = <String>[];
      final unconfigured = Diagnostics(fallback: messages.add);
      await unconfigured.record(
        source: DiagnosticSource.flutter,
        event: DiagnosticEvent.reported,
        operation: 'report_error',
        code: 'unexpected',
      );
      expect(messages.single, contains('no library directory'));
      final broken = Diagnostics(
        fallback: (_) => throw const FileSystemException('stderr closed'),
      );
      await expectLater(
        broken.record(
          source: DiagnosticSource.flutter,
          event: DiagnosticEvent.failed,
          operation: 'initialize',
        ),
        completes,
      );
      await expectLater(broken.flush(), completes);
    },
  );

  test(
    'native bridge success and validation failures persist without request payloads',
    () async {
      final backend = Backend(dataRoot: directory.path);
      await backend.call('snapshot');
      await expectLater(
        backend.call('update_settings', {
          'values': {
            'MirrorUrl':
                'https://username:private-password@example.invalid/?token=private-token',
          },
        }),
        throwsA(isA<BackendException>()),
      );
      final events = await readEvents(directory);
      expect(events[0]['source'], 'bridge');
      expect(events[0]['event'], 'completed');
      expect(events[1]['source'], 'core');
      expect(events[1]['code'], 'validation');
      expect(events[1]['operation'], 'update_settings');
      expect(events.toString(), isNot(contains('private-password')));
      expect(events.toString(), isNot(contains('private-token')));
    },
  );

  testWidgets(
    'unavailable diagnostic sinks do not leave the UI busy or crash',
    (tester) async {
      final log = Diagnostics(
        fallback: (_) => throw const FileSystemException('stderr closed'),
      );
      final controller = AppController(FailingDiagnosticBackend(log));
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: AnimatedBuilder(
              animation: controller,
              builder: (context, _) => FilledButton(
                onPressed: controller.busy
                    ? null
                    : () => controller.execute('search_idgames'),
                child: Text(controller.busy ? 'Working' : 'Search'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('Search'));
      await tester.pumpAndSettle();
      await log.flush();
      expect(find.text('Search'), findsOneWidget);
      expect(controller.busy, isFalse);
      expect(controller.error, contains('private-password'));
      expect(tester.takeException(), isNull);
      controller.dispose();
    },
  );
}
