import 'dart:async';
import 'package:doomedlauncher/bridge/backend.dart';
import 'package:doomedlauncher/state/app_controller.dart';
import 'package:doomedlauncher/ui/idgames.dart';
import 'package:doomedlauncher/ui/management.dart';
import 'package:doomedlauncher/ui/management/helpers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class FakeManagementBackend extends Backend {
  FakeManagementBackend() : super(dataRoot: '/tmp/test-library');
  final requests = <({String op, Map<String, dynamic> args})>[];
  final Map<String, dynamic> state = {
    'games': <Map<String, dynamic>>[],
    'ports': <Map<String, dynamic>>[],
    'iwads': <Map<String, dynamic>>[],
    'tags': <Map<String, dynamic>>[],
    'settings': <String, dynamic>{},
    'data_root': '/tmp/test-library',
  };
  FutureOr<Map<String, dynamic>> Function(String op, Map<String, dynamic> args)?
  handler;
  @override
  Future<void> initialize() async {}
  @override
  Future<Map<String, dynamic>> call(
    String op, [
    Map<String, dynamic> args = const {},
  ]) async {
    requests.add((op: op, args: args));
    if (op == 'snapshot') return state;
    return await handler?.call(op, args) ?? {};
  }

  AppController controller() => AppController(this)..snapshot = state;
}

Future<void> mountPage(
  WidgetTester tester,
  Widget page, {
  double textScale = 1,
}) async {
  tester.view.physicalSize = const Size(1280, 1000);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
  await tester.pumpWidget(
    MaterialApp(
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(
          context,
        ).copyWith(textScaler: TextScaler.linear(textScale)),
        child: child!,
      ),
      home: Scaffold(body: page),
    ),
  );
  await tester.pumpAndSettle();
}

const archiveFile = {
  'id': 1,
  'file_name': 'fresh.zip',
  'directory': 'levels/doom2/',
  'title': 'Fresh map',
  'author': 'Mapper',
  'description': 'A fresh map.',
};

void main() {
  testWidgets(
    'pending download admission prevents shutdown and a late completion cannot import while closing',
    (tester) async {
      final backend = FakeManagementBackend();
      final started = Completer<Map<String, dynamic>>();
      final polled = Completer<Map<String, dynamic>>();
      final played = <int>[];
      backend.handler = (op, args) {
        if (op == 'search_idgames') {
          return {
            'files': [archiveFile],
          };
        }
        if (op == 'start_download') return started.future;
        if (op == 'download_status') return polled.future;
        return {};
      };
      final controller = backend.controller();
      await mountPage(
        tester,
        IdgamesPage(controller: controller, onPlayEntry: played.add),
      );
      await tester.tap(find.byTooltip('Download and play'));
      await tester.pump();
      expect(controller.busy, isTrue);
      expect(controller.beginShutdown(), isFalse);
      started.complete({
        'download': {'id': 'job', 'status': 'downloading'},
      });
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));
      expect(controller.beginShutdown(), isTrue);
      polled.complete({
        'download': {
          'id': 'job',
          'status': 'completed',
          'local_path': '/tmp/fresh.zip',
        },
      });
      await tester.pumpAndSettle();
      expect(
        backend.requests.where((item) => item.op == 'import_files'),
        isEmpty,
      );
      expect(played, isEmpty);
      expect(controller.shutdownInProgress, isTrue);
      expect(find.textContaining('launcher is closing'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      controller.dispose();
    },
  );

  test(
    'download play resolves only the exact files returned by this import',
    () {
      final games = [
        {'GameFileID': 900, 'FileName': 'unrelated.zip'},
        {'GameFileID': 12, 'FileName': 'fresh.zip'},
      ];
      expect(importedEntryId(['fresh.zip'], games), 12);
      expect(importedEntryId(['unknown.zip'], games), isNull);
      expect(importedEntryId(['fresh.zip', 'unrelated.zip'], games), isNull);
    },
  );

  test('network settings reject unsupported or credential-bearing URLs', () {
    expect(validateWebUrl('https://example.org/idgames/'), isNull);
    expect(validateWebUrl('ftp://example.org/'), isNotNull);
    expect(validateWebUrl('https://secret@example.org/'), isNotNull);
    expect(validateWebUrl('not a URL'), isNotNull);
  });

  testWidgets(
    'source port editor preserves all launch fields and Doom 64 type',
    (tester) async {
      final backend = FakeManagementBackend();
      final port = {
        'id': 7,
        'name': 'Doom 64',
        'executable': '/games/Doom 64/Doom64.exe',
        'directory': '/games/Doom 64',
        'supported_extensions': '.wad',
        'file_option': '-file',
        'extra_parameters': '--flag "with spaces"',
        'alt_save_directory': '/saves',
        'launch_type': 2,
      };
      await mountPage(
        tester,
        SourcePortEditDialog(controller: backend.controller(), port: port),
      );
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Name'),
        'Renamed Doom 64',
      );
      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();
      final saved =
          backend.requests
                  .singleWhere((item) => item.op == 'save_port')
                  .args['port']
              as Map;
      expect(saved, {...port, 'name': 'Renamed Doom 64'});
    },
  );

  testWidgets('empty source port cannot be saved', (tester) async {
    final backend = FakeManagementBackend();
    await mountPage(
      tester,
      SourcePortEditDialog(controller: backend.controller()),
    );
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();
    expect(find.text('Name is required.'), findsOneWidget);
    expect(backend.requests.where((item) => item.op == 'save_port'), isEmpty);
  });

  testWidgets(
    'tag creation validates name and sends selected library-tab option',
    (tester) async {
      final backend = FakeManagementBackend();
      await mountPage(tester, TagsPage(controller: backend.controller()));
      await tester.tap(find.text('New tag'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();
      expect(find.text('Enter a tag name.'), findsOneWidget);
      await tester.enterText(find.byType(TextFormField), ' Favorites ');
      await tester.tap(find.text('Show as library tab'));
      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();
      expect(
        backend.requests.singleWhere((item) => item.op == 'save_tag').args,
        {'name': 'Favorites', 'has_tab': true},
      );
    },
  );

  testWidgets(
    'store scanning imports IWADs and add-ons separately and adds Doom64',
    (tester) async {
      final backend = FakeManagementBackend();
      backend.handler = (op, args) {
        if (op == 'scan_stores') {
          return {
            'iwads': ['/games/DOOM2.WAD'],
            'pwads': ['/games/addon.wad'],
            'doom64_exe': '/games/Doom64.exe',
          };
        }
        if (op == 'import_files') return {'added': args['paths'], 'failed': []};
        return {};
      };
      await mountPage(
        tester,
        StoreScanDialog(controller: backend.controller()),
      );
      await tester.tap(find.text('Import selected'));
      await tester.pumpAndSettle();
      final imports = backend.requests
          .where((item) => item.op == 'import_files')
          .toList();
      expect(imports.length, 2);
      expect(imports[0].args, {
        'paths': ['/games/DOOM2.WAD'],
        'as_iwads': true,
      });
      expect(imports[1].args, {
        'paths': ['/games/addon.wad'],
        'as_iwads': false,
      });
      expect(
        (backend.requests
                .singleWhere((item) => item.op == 'save_port')
                .args['port']
            as Map)['launch_type'],
        2,
      );
    },
  );

  testWidgets('idgames ignores late search responses', (tester) async {
    final backend = FakeManagementBackend();
    final first = Completer<Map<String, dynamic>>();
    backend.handler = (op, args) {
      if (op == 'search_idgames' && args['query'] == 'first') {
        return first.future;
      }
      if (op == 'search_idgames' && args['query'] == 'second') {
        return {
          'files': [
            {...archiveFile, 'title': 'Second result'},
          ],
        };
      }
      return {'files': []};
    };
    await mountPage(tester, IdgamesPage(controller: backend.controller()));
    await tester.enterText(find.byType(TextField), 'first');
    await tester.tap(find.text('Search'));
    await tester.pump();
    await tester.enterText(find.byType(TextField), 'second');
    await tester.tap(find.text('Search'));
    await tester.pumpAndSettle();
    first.complete({
      'files': [
        {...archiveFile, 'title': 'Stale first result'},
      ],
    });
    await tester.pumpAndSettle();
    expect(find.text('Second result'), findsOneWidget);
    expect(find.text('Stale first result'), findsNothing);
  });

  testWidgets(
    'download imports once and plays only its own entry after cleanup',
    (tester) async {
      final backend = FakeManagementBackend();
      final played = <int>[];
      backend.state['games'] = [
        {'GameFileID': 999, 'FileName': 'unrelated.zip'},
      ];
      backend.handler = (op, args) {
        if (op == 'search_idgames') {
          return {
            'files': [archiveFile],
          };
        }
        if (op == 'start_download') {
          return {
            'download': {
              'id': 42,
              'status': 'completed',
              'local_path': '/tmp/fresh.zip',
            },
          };
        }
        if (op == 'import_files') {
          backend.state['games'] = [
            ...backend.state['games'] as List,
            {'GameFileID': 12, 'FileName': 'fresh.zip'},
          ];
          return {
            'added': ['fresh.zip'],
            'failed': [],
          };
        }
        return {};
      };
      await mountPage(
        tester,
        IdgamesPage(controller: backend.controller(), onPlayEntry: played.add),
      );
      await tester.tap(find.byTooltip('Download and play'));
      await tester.pumpAndSettle();
      expect(played, [12]);
      expect(
        backend.requests.where((item) => item.op == 'import_files'),
        hasLength(1),
      );
      expect(
        backend.requests
            .where((item) => item.op == 'discard_download')
            .single
            .args,
        {'id': 42},
      );
      expect(find.text('Imported 1 file into your library.'), findsOneWidget);
    },
  );

  testWidgets(
    'failed import keeps downloaded file and retry does not download again',
    (tester) async {
      final backend = FakeManagementBackend();
      var attempts = 0;
      backend.handler = (op, args) {
        if (op == 'search_idgames') {
          return {
            'files': [archiveFile],
          };
        }
        if (op == 'start_download') {
          return {
            'download': {
              'id': 42,
              'status': 'completed',
              'local_path': '/tmp/fresh.zip',
            },
          };
        }
        if (op == 'import_files') {
          if (++attempts == 1) throw const BackendException('io', 'Disk full');
          backend.state['games'] = [
            {'GameFileID': 12, 'FileName': 'fresh.zip'},
          ];
          return {
            'added': ['fresh.zip'],
            'failed': [],
          };
        }
        return {};
      };
      await mountPage(tester, IdgamesPage(controller: backend.controller()));
      await tester.tap(find.byTooltip('Download and import'));
      await tester.pumpAndSettle();
      expect(find.text('Retry import'), findsOneWidget);
      expect(
        backend.requests.where((item) => item.op == 'discard_download'),
        isEmpty,
      );
      await tester.tap(find.text('Retry import'));
      await tester.pumpAndSettle();
      expect(attempts, 2);
      expect(
        backend.requests.where((item) => item.op == 'start_download'),
        hasLength(1),
      );
      expect(
        backend.requests.where((item) => item.op == 'discard_download'),
        hasLength(1),
      );
    },
  );

  testWidgets(
    'disposing during download creation cancels its job without importing',
    (tester) async {
      final backend = FakeManagementBackend();
      final started = Completer<Map<String, dynamic>>();
      backend.handler = (op, args) {
        if (op == 'search_idgames') {
          return {
            'files': [archiveFile],
          };
        }
        if (op == 'start_download') return started.future;
        return {};
      };
      await mountPage(tester, IdgamesPage(controller: backend.controller()));
      await tester.tap(find.byTooltip('Download and import'));
      await tester.pump();
      await tester.pumpWidget(const SizedBox());
      started.complete({
        'download': {
          'id': 42,
          'status': 'completed',
          'local_path': '/tmp/fresh.zip',
        },
      });
      await tester.pumpAndSettle();
      expect(
        backend.requests
            .where((item) => item.op == 'cancel_download')
            .single
            .args,
        {'id': 42},
      );
      expect(
        backend.requests.where((item) => item.op == 'import_files'),
        isEmpty,
      );
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('cancelling a download ignores an already pending completion', (
    tester,
  ) async {
    final backend = FakeManagementBackend();
    final polled = Completer<Map<String, dynamic>>();
    final played = <int>[];
    backend.handler = (op, args) {
      if (op == 'search_idgames') {
        return {
          'files': [archiveFile],
        };
      }
      if (op == 'start_download') {
        return {
          'download': {
            'id': 'job',
            'status': 'downloading',
            'received': 1,
            'total': 100,
          },
        };
      }
      if (op == 'download_status') return polled.future;
      if (op == 'cancel_download') {
        return {
          'download': {'id': 'job', 'status': 'cancelled'},
        };
      }
      return {};
    };
    await mountPage(
      tester,
      IdgamesPage(controller: backend.controller(), onPlayEntry: played.add),
    );
    await tester.tap(find.byTooltip('Download and play'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 500));
    await tester.tap(find.text('Cancel'));
    await tester.pump();
    polled.complete({
      'download': {
        'id': 'job',
        'status': 'completed',
        'local_path': '/tmp/fresh.zip',
      },
    });
    await tester.pumpAndSettle();
    expect(find.text('Download cancelled.'), findsOneWidget);
    expect(
      backend.requests.where((item) => item.op == 'import_files'),
      isEmpty,
    );
    expect(played, isEmpty);
  });

  testWidgets(
    'download settings retain edits on refresh and validate before saving',
    (tester) async {
      final backend = FakeManagementBackend();
      final controller = backend.controller();
      backend.handler = (op, args) {
        if (op == 'update_settings') {
          (backend.state['settings'] as Map).addAll(args['values'] as Map);
        }
        return {};
      };
      await mountPage(tester, SettingsPage(controller: controller));
      final mirror = find.widgetWithText(TextFormField, 'Download mirror URL');
      await tester.ensureVisible(mirror);
      await tester.enterText(mirror, 'ftp://invalid.example/idgames/');
      await controller.refresh();
      await tester.pump();
      expect(find.text('ftp://invalid.example/idgames/'), findsOneWidget);
      await tester.ensureVisible(find.text('Save download settings'));
      await tester.tap(find.text('Save download settings'));
      await tester.pumpAndSettle();
      expect(
        backend.requests.where((item) => item.op == 'update_settings'),
        isEmpty,
      );
      await tester.ensureVisible(mirror);
      await tester.enterText(mirror, 'https://archive.example/idgames/');
      await tester.ensureVisible(find.text('Save download settings'));
      await tester.tap(find.text('Save download settings'));
      await tester.pumpAndSettle();
      final values =
          backend.requests
                  .singleWhere((item) => item.op == 'update_settings')
                  .args['values']
              as Map;
      expect(values['MirrorUrl'], 'https://archive.example/idgames/');
      expect(values['IdGamesUrl'], 'https://www.doomworld.com/idgames/');
      expect(values['ApiPage'], 'api/api.php');
    },
  );

  testWidgets('idgames search controls fit a narrow desktop pane', (
    tester,
  ) async {
    final backend = FakeManagementBackend();
    backend.handler = (op, args) => {
      'files': [archiveFile],
    };
    await mountPage(
      tester,
      IdgamesPage(controller: backend.controller(), onPlayEntry: (_) {}),
    );
    tester.view.physicalSize = const Size(640, 900);
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.byTooltip('Download and play'), findsOneWidget);
  });

  testWidgets('failed import remains actionable until explicitly discarded', (
    tester,
  ) async {
    final backend = FakeManagementBackend();
    final discarded = Completer<Map<String, dynamic>>();
    backend.handler = (op, args) {
      if (op == 'search_idgames') {
        return {
          'files': [
            archiveFile,
            {...archiveFile, 'file_name': 'other.zip', 'title': 'Other map'},
          ],
        };
      }
      if (op == 'start_download') {
        return {
          'download': {
            'id': 'job',
            'status': 'completed',
            'local_path': '/tmp/fresh.zip',
          },
        };
      }
      if (op == 'import_files') throw const BackendException('io', 'Disk full');
      if (op == 'discard_download') return discarded.future;
      return {};
    };
    await mountPage(tester, IdgamesPage(controller: backend.controller()));
    await tester.tap(find.byTooltip('Download and import').first);
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<IconButton>(
            find.widgetWithIcon(IconButton, Icons.download).last,
          )
          .onPressed,
      isNull,
    );
    await tester.tap(find.text('Discard download'));
    await tester.pump();
    await tester.tap(find.text('Retry import'));
    await tester.pump();
    expect(
      backend.requests.where((item) => item.op == 'import_files'),
      hasLength(1),
    );
    discarded.complete({});
    await tester.pumpAndSettle();
    expect(
      backend.requests
          .where((item) => item.op == 'discard_download')
          .single
          .args,
      {'id': 'job'},
    );
    expect(
      tester
          .widget<IconButton>(
            find.widgetWithIcon(IconButton, Icons.download).last,
          )
          .onPressed,
      isNotNull,
    );
  });

  testWidgets(
    'failed download cleanup after disposal surfaces a controller error',
    (tester) async {
      final backend = FakeManagementBackend();
      final controller = backend.controller();
      backend.handler = (op, args) {
        if (op == 'search_idgames') {
          return {
            'files': [archiveFile],
          };
        }
        if (op == 'start_download') {
          return {
            'download': {'id': 'job', 'status': 'downloading'},
          };
        }
        if (op == 'cancel_download') {
          throw const BackendException('io', 'Cancellation failed');
        }
        return {};
      };
      await mountPage(tester, IdgamesPage(controller: controller));
      await tester.tap(find.byTooltip('Download and import'));
      await tester.pump();
      await tester.pumpWidget(const SizedBox());
      await tester.pumpAndSettle();
      expect(controller.error, contains('Cancellation failed'));
    },
  );

  testWidgets('idgames remains scrollable with large text in a small window', (
    tester,
  ) async {
    final backend = FakeManagementBackend();
    backend.handler = (op, args) => {
      'files': [archiveFile],
    };
    await mountPage(
      tester,
      IdgamesPage(controller: backend.controller()),
      textScale: 2,
    );
    tester.view.physicalSize = const Size(760, 560);
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('Fresh map'),
      150,
      scrollable: find
          .descendant(
            of: find.byType(CustomScrollView),
            matching: find.byType(Scrollable),
          )
          .first,
    );
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.text('Fresh map'), findsOneWidget);
  });

  testWidgets('source port backend error keeps editor values for correction', (
    tester,
  ) async {
    final backend = FakeManagementBackend();
    backend.handler = (op, args) {
      if (op == 'save_port') {
        throw const BackendException(
          'validation',
          'The save directory must be an absolute path.',
        );
      }
      return {};
    };
    await mountPage(
      tester,
      SourcePortEditDialog(controller: backend.controller()),
    );
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Name'),
      'My port',
    );
    await tester.enterText(
      find.widgetWithText(TextFormField, 'Executable'),
      'gzdoom',
    );
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();
    expect(find.text('My port'), findsOneWidget);
    expect(
      find.text('The save directory must be an absolute path.'),
      findsOneWidget,
    );
    expect(find.text('Save'), findsOneWidget);
  });

  testWidgets('settings remain accessible when backend initialization fails', (
    tester,
  ) async {
    final controller = AppController(Backend());
    await mountPage(tester, SettingsPage(controller: controller));
    await tester.ensureVisible(find.text('Advanced'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Advanced'));
    await tester.pumpAndSettle();
    expect(find.text('The library is not open.'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
