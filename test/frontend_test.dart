import 'dart:async';
import 'package:doomedlauncher/app.dart';
import 'package:doomedlauncher/main.dart'
    show prepareLauncherShutdown, handleDesktopLifecycleCall, DesktopLifecycle;
import 'package:doomedlauncher/bridge/backend.dart';
import 'package:doomedlauncher/state/app_controller.dart';
import 'package:doomedlauncher/ui/edit_dialog.dart';
import 'package:doomedlauncher/ui/library_page.dart';
import 'package:doomedlauncher/ui/play_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

class FrontendBackend extends Backend {
  FrontendBackend() : super(dataRoot: '/tmp/frontend-test');
  final requests = <(String, Map<String, dynamic>)>[];
  Map<String, dynamic> activeJobs = {'sessions': [], 'downloads': []};
  bool failShutdown = false;
  Completer<Map<String, dynamic>>? jobsGate;
  final game = <String, dynamic>{
    'GameFileID': 1,
    'FileName': 'adventure.wad',
    'Title': 'Adventure',
    'Author': 'Mapper',
    'Rating': 3,
    'tagIds': [2],
    'isIwad': false,
  };
  late final state = <String, dynamic>{
    'games': [game],
    'ports': [
      {'id': 3, 'name': 'Test engine'},
    ],
    'iwads': [
      {'iwad_id': 4, 'name': 'Doom II', 'game_file_id': 9},
    ],
    'tags': [
      {'tag_id': 2, 'name': 'Favorites', 'has_tab': true},
    ],
    'settings': {'ColorThemeType': 'Dark'},
    'sessions': [],
  };
  @override
  Future<void> initialize() async {}
  @override
  Future<Map<String, dynamic>> call(
    String op, [
    Map<String, dynamic> args = const {},
  ]) async {
    requests.add((op, args));
    if (op == 'active_jobs' && jobsGate != null) return jobsGate!.future;
    if (op == 'shutdown' && failShutdown) {
      throw const BackendException('shutdown', 'A game could not be stopped.');
    }
    return switch (op) {
      'snapshot' => state,
      'active_jobs' => activeJobs,
      'entry_details' => {
        'game': game,
        'files': [],
        'stats': [],
        'text_files': [],
      },
      'play_defaults' => {
        'options': {
          'game_file_id': 1,
          'source_port_id': 3,
          'iwad_id': 4,
          'map': 'MAP02',
          'skill': 4,
          'additional_files': ['first.wad', 'second.wad'],
          'remember': true,
        },
        'maps': ['MAP01', 'MAP02'],
      },
      'launch_preview' => {
        'display': 'engine -iwad doom2.wad -file adventure.wad',
      },
      'launch' => {
        'session': {'id': 1, 'status': 'running'},
      },
      'search_idgames' => {'files': []},
      _ => {},
    };
  }
}

void main() {
  test(
    'recent means newest 25 downloads, filters IWADs and preserves numeric rating order',
    () {
      final games = List.generate(
        30,
        (i) => <String, dynamic>{
          'GameFileID': i,
          'FileName': '$i.wad',
          'Downloaded': '2026-01-${(i + 1).toString().padLeft(2, '0')}',
          'isIwad': false,
          'Rating': i % 6,
          'tagIds': i.isEven ? [2] : [],
        },
      );
      games.add({
        'GameFileID': 90,
        'FileName': 'doom.wad',
        'isIwad': true,
        'tagIds': [],
      });
      final recent = filterLibrary(games, filter: 'recent');
      expect(recent.length, 25);
      expect(recent.any((g) => g['GameFileID'] == 0), false);
      expect(filterLibrary(games, filter: 'iwads').single['GameFileID'], 90);
      expect(filterLibrary(games, tagId: 2).length, 15);
      expect(filterLibrary(games, filter: 'untagged').length, 15);
      expect(
        filterLibrary(games, sort: 'Rating', descending: true).first['Rating'],
        5,
      );
      expect(filterLibrary(games, query: 'doom'), isEmpty);
    },
  );

  testWidgets('editing a draft and cancelling never writes metadata', (
    tester,
  ) async {
    final backend = FrontendBackend(),
        controller = AppController(FrontendBackend());
    controller.snapshot = backend.state;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => TextButton(
              onPressed: () => showEditEntry(context, controller, backend.game),
              child: const Text('Edit'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Edit'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.widgetWithText(TextField, 'Adventure'),
      'Changed draft',
    );
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(
      (controller.backend as FrontendBackend).requests.where(
        (r) => r.$1 == 'edit_entry',
      ),
      isEmpty,
    );
    expect(backend.game['Title'], 'Adventure');
    controller.dispose();
  });

  testWidgets('play keeps ordered additional files and full launch options', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1300, 1400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final backend = FrontendBackend(),
        controller = AppController(FrontendBackend());
    final actualBackend = controller.backend as FrontendBackend;
    controller.snapshot = backend.state;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: PlayEntryDialog(controller: controller, game: backend.game),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byTooltip('Move up').last);
    await tester.tap(find.byTooltip('Move up').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Launch'));
    await tester.pumpAndSettle();
    final request =
        actualBackend.requests
                .singleWhere((r) => r.$1 == 'launch')
                .$2['options']
            as Map;
    expect(request['additional_files'], ['second.wad', 'first.wad']);
    expect(request['source_port_id'], 3);
    expect(request['iwad_id'], 4);
    expect(request['map'], 'MAP02');
    expect(request['skill'], 4);
    expect(request['remember'], true);
    expect(request['save_statistics'], true);
    await tester.pumpWidget(const SizedBox.shrink());
    controller.dispose();
  });

  testWidgets('desktop shell navigates and stays within compact viewport', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(800, 650);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final backend = FrontendBackend(),
        controller = AppController(FrontendBackend());
    controller.snapshot = backend.state;
    await tester.pumpWidget(DoomLauncherApp(controller: controller));
    await tester.pumpAndSettle();
    expect(find.text('Your library'), findsOneWidget);
    expect(find.text('Adventure'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('Tags').last);
    await tester.pumpAndSettle();
    expect(find.text('Favorites'), findsWidgets);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox.shrink());
    controller.dispose();
  });

  testWidgets(
    'Doom 64 starts with standalone parameters and hides incompatible options',
    (tester) async {
      final backend = FrontendBackend();
      (backend.state['ports'] as List).single['launch_type'] = 2;
      final controller = AppController(backend)..snapshot = backend.state;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: PlayEntryDialog(controller: controller, game: backend.game),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Base game (IWAD)'), findsNothing);
      expect(find.text('Record demo'), findsNothing);
      expect(
        find.textContaining('Doom 64 starts as a standalone'),
        findsOneWidget,
      );
      await tester.tap(find.text('Launch'));
      await tester.pumpAndSettle();
      final options =
          backend.requests.singleWhere((r) => r.$1 == 'launch').$2['options']
              as Map;
      expect(options['extra_params_only'], true);
      expect(options['iwad_id'], isNull);
      expect(options['map'], '');
      expect(options['additional_files'], isEmpty);
      expect(options['save_statistics'], false);
      await tester.pumpWidget(const SizedBox.shrink());
      controller.dispose();
    },
  );

  testWidgets(
    'quit cancellation preserves jobs and confirmation settles them',
    (tester) async {
      final backend = FrontendBackend()
        ..activeJobs = {
          'sessions': [
            {'id': 1},
          ],
          'downloads': [
            {'id': 2},
          ],
        };
      final controller = AppController(backend)..snapshot = backend.state;
      bool? result;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: Builder(
              builder: (context) => TextButton(
                onPressed: () async {
                  result = await prepareLauncherShutdown(
                    controller,
                    context: context,
                  );
                },
                child: const Text('Quit'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('Quit'));
      await tester.pumpAndSettle();
      expect(find.text('Stop active work and quit?'), findsOneWidget);
      await tester.tap(find.text('Cancel'));
      await tester.pumpAndSettle();
      expect(result, false);
      expect(backend.requests.where((r) => r.$1 == 'shutdown'), isEmpty);
      await tester.tap(find.text('Quit'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Stop and quit'));
      await tester.pumpAndSettle();
      expect(result, true);
      expect(backend.requests.where((r) => r.$1 == 'shutdown'), hasLength(1));
      controller.dispose();
    },
  );

  test(
    'shutdown failure keeps application open and reports the error',
    () async {
      final backend = FrontendBackend()..failShutdown = true;
      final controller = AppController(backend);
      expect(await prepareLauncherShutdown(controller), false);
      expect(controller.error, 'A game could not be stopped.');
      controller.dispose();
    },
  );

  testWidgets(
    'unavailable games retain metadata and editing while play is disabled',
    (tester) async {
      final backend = FrontendBackend();
      backend.game['pathError'] =
          'The original game file is unavailable on this computer.';
      final controller = AppController(backend)..snapshot = backend.state;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: LibraryPage(
              controller: controller,
              onImportFiles: () {},
              onImportDirectory: () {},
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Adventure'));
      await tester.pumpAndSettle();
      final play = tester.widget<FilledButton>(
        find.widgetWithText(FilledButton, 'Play'),
      );
      expect(play.onPressed, isNull);
      expect(find.byIcon(Icons.warning_amber_rounded), findsOneWidget);
      await tester.tap(find.byTooltip('Actions for Adventure'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Edit metadata…'));
      await tester.pumpAndSettle();
      expect(find.text('Edit library entry'), findsOneWidget);
      expect(find.widgetWithText(TextField, 'Adventure'), findsOneWidget);
      await tester.pumpWidget(const SizedBox.shrink());
      controller.dispose();
    },
  );

  test(
    'tag tabs include tagged IWADs and Recent searches before limiting to 25',
    () {
      final rows = List.generate(
        30,
        (i) => <String, dynamic>{
          'GameFileID': i + 1,
          'FileName': 'newer-$i.wad',
          'Downloaded': '2026-02-${(i + 1).toString().padLeft(2, '0')}',
          'isIwad': false,
          'tagIds': [],
        },
      );
      rows.add({
        'GameFileID': 90,
        'FileName': 'old-matching.wad',
        'Downloaded': '2025-01-01',
        'isIwad': false,
        'tagIds': [],
      });
      rows.add({
        'GameFileID': 91,
        'FileName': 'tagged-base.wad',
        'isIwad': true,
        'tagIds': [2],
      });
      expect(
        filterLibrary(rows, filter: 'tag-2', tagId: 2).single['GameFileID'],
        91,
      );
      expect(
        filterLibrary(
          rows,
          filter: 'recent',
          query: 'matching',
        ).single['GameFileID'],
        90,
      );
      expect(filterLibrary(rows, filter: 'recent').length, 25);
    },
  );

  test(
    'mixed legacy, UTC and offset dates sort by instant with stable unknowns',
    () {
      final sameInstant = DateTime(2026, 10, 9, 23).toUtc().toIso8601String();
      final dates = <int, String?>{
        8: 'unknown',
        7: null,
        6: '',
        5: sameInstant,
        4: '2026-10-09 23:00:00',
        3: '2026-10-09T02:00:00Z',
        2: '2026-10-09T02:30:00+02:00',
      };
      for (final column in ['LastPlayed', 'Downloaded', 'ReleaseDate']) {
        final rows = [
          for (final entry in dates.entries)
            <String, dynamic>{'GameFileID': entry.key, column: entry.value},
        ];
        expect(
          filterLibrary(rows, sort: column).map((row) => row['GameFileID']),
          [6, 7, 8, 2, 3, 4, 5],
          reason:
              '$column orders equivalent local and UTC instants by stable ID',
        );
        expect(
          filterLibrary(
            rows,
            sort: column,
            descending: true,
          ).map((row) => row['GameFileID']),
          [4, 5, 3, 2, 6, 7, 8],
          reason:
              '$column keeps unknown dates after known values when descending',
        );
      }
    },
  );

  test('Recent chooses 25 chronological imports across timestamp formats', () {
    final rows = <Map<String, dynamic>>[
      {'GameFileID': 1, 'Downloaded': '2026-10-09 23:00:00'},
      for (var id = 2; id <= 26; id++)
        {'GameFileID': id, 'Downloaded': '2026-10-09T02:00:00Z'},
      {'GameFileID': 27, 'Downloaded': 'unknown'},
    ];
    final recent = filterLibrary(
      rows,
      filter: 'recent',
      sort: 'Downloaded',
      descending: true,
    );
    expect(recent.map((row) => row['GameFileID']), [
      1,
      ...List.generate(24, (index) => index + 2),
    ]);
  });

  test('legacy null values sort as unrated and empty text', () {
    final rows = <Map<String, dynamic>>[
      {
        'GameFileID': 1,
        'FileName': 'missing.wad',
        'Rating': null,
        'Author': null,
      },
      {'GameFileID': 2, 'FileName': 'rated.wad', 'Rating': 5, 'Author': 'Ada'},
      {
        'GameFileID': 3,
        'FileName': 'unrated.wad',
        'Rating': 0,
        'Author': 'Zed',
      },
    ];
    expect(
      filterLibrary(
        rows,
        sort: 'Rating',
        descending: true,
      ).map((g) => g['GameFileID']),
      [2, 1, 3],
    );
    expect(filterLibrary(rows, sort: 'Author').map((g) => g['GameFileID']), [
      1,
      2,
      3,
    ]);
  });

  testWidgets(
    'sorting preserves selected ID while searching it away clears actions',
    (tester) async {
      final backend = FrontendBackend();
      (backend.state['games'] as List).add({
        'GameFileID': 2,
        'Title': 'Before Adventure',
        'FileName': 'before.wad',
        'Author': 'AAA',
        'tagIds': [],
        'isIwad': false,
      });
      final controller = AppController(backend)..snapshot = backend.state;
      final key = GlobalKey<LibraryPageState>();
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: LibraryPage(
              key: key,
              controller: controller,
              onImportFiles: () {},
              onImportDirectory: () {},
            ),
          ),
        ),
      );
      await tester.tap(find.text('Adventure'));
      await tester.pump(
        const Duration(milliseconds: 350),
      ); // Resolve single tap versus double-click play.
      await tester.pumpAndSettle();
      expect(key.currentState!.selected?['GameFileID'], 1);
      await tester.tap(find.byTooltip('Sort library'));
      await tester.pumpAndSettle();
      await tester.tap(
        find.byWidgetPredicate(
          (widget) =>
              widget is CheckedPopupMenuItem<String> &&
              widget.value == 'Author',
        ),
      );
      await tester.pumpAndSettle();
      expect(key.currentState!.selected?['GameFileID'], 1);
      expect(
        tester.getTopLeft(find.text('Before Adventure')).dy,
        lessThan(tester.getTopLeft(find.text('Adventure')).dy),
      );
      await tester.enterText(find.byType(TextField), 'no matching title');
      await tester.pumpAndSettle();
      expect(key.currentState!.selected, isNull);
      expect(
        tester
            .widget<FilledButton>(find.widgetWithText(FilledButton, 'Play'))
            .onPressed,
        isNull,
      );
      await key.currentState!.deleteSelected();
      await tester.pumpAndSettle();
      expect(find.text('Delete library entry?'), findsNothing);
      await tester.enterText(find.byType(TextField), '');
      await tester.pumpAndSettle();
      expect(key.currentState!.selected, isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      controller.dispose();
    },
  );

  testWidgets(
    'macOS native Settings and About use app handlers, Quit requests guarded close',
    (tester) async {
      debugDefaultTargetPlatformOverride = TargetPlatform.macOS;
      addTearDown(() => debugDefaultTargetPlatformOverride = null);
      final methods = <String>[];
      final messenger =
          TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
      messenger.setMockMethodCallHandler(
        SystemChannels.menu,
        (_) async => null,
      );
      messenger.setMockMethodCallHandler(
        const MethodChannel('window_manager'),
        (call) async {
          methods.add(call.method);
          return null;
        },
      );
      addTearDown(() {
        messenger.setMockMethodCallHandler(SystemChannels.menu, null);
        messenger.setMockMethodCallHandler(
          const MethodChannel('window_manager'),
          null,
        );
      });
      final backend = FrontendBackend();
      final controller = AppController(backend)..snapshot = backend.state;
      await tester.pumpWidget(DoomLauncherApp(controller: controller));
      await tester.pumpAndSettle();
      Iterable<PlatformMenuItem> visit(PlatformMenuItem menu) sync* {
        yield menu;
        for (final child in menu is PlatformMenu ? menu.menus : menu.members) {
          yield* visit(child);
        }
      }

      PlatformMenuItem item(String label) => tester
          .widget<PlatformMenuBar>(find.byType(PlatformMenuBar))
          .menus
          .expand(visit)
          .firstWhere((item) => item.label == label);
      expect(find.byType(MenuBar), findsNothing);
      item('Settings…').onSelected!();
      await tester.pumpAndSettle();
      expect(
        find.text(
          'Customize the library, launch defaults, and download sources.',
        ),
        findsOneWidget,
      );
      item('About Doom Launcher').onSelected!();
      await tester.pumpAndSettle();
      expect(find.byType(AboutDialog), findsOneWidget);
      Navigator.of(tester.element(find.byType(AboutDialog))).pop();
      await tester.pumpAndSettle();
      item('Quit Doom Launcher').onSelected!();
      await tester.pumpAndSettle();
      expect(methods, ['close']);
      expect(
        backend.requests.where((request) => request.$1 == 'delete_entry'),
        isEmpty,
      );
      await tester.pumpWidget(const SizedBox.shrink());
      controller.dispose();
      debugDefaultTargetPlatformOverride = null;
    },
  );

  test(
    'native Dock quit honors a cancelled shutdown response without importing files',
    () async {
      final backend = FrontendBackend(),
          controller = AppController(FrontendBackend());
      var requests = 0;
      expect(
        await handleDesktopLifecycleCall(
          const MethodCall('requestQuit', {'requestId': 'quit-fixture'}),
          controller: controller,
          onQuit: (requestId) async {
            expect(requestId, 'quit-fixture');
            requests++;
            return false;
          },
        ),
        false,
      );
      expect(requests, 1);
      expect((controller.backend as FrontendBackend).requests, isEmpty);
      expect(
        await handleDesktopLifecycleCall(
          const MethodCall('requestQuit', {'requestId': 'quit-fixture'}),
          controller: controller,
          onQuit: (_) async => true,
        ),
        true,
      );
      expect(backend.requests, isEmpty);
      controller.dispose();
    },
  );

  test(
    'shutdown blocks new imports and launches before checking native jobs',
    () async {
      final backend = FrontendBackend()
        ..jobsGate = Completer<Map<String, dynamic>>();
      final controller = AppController(backend)..snapshot = backend.state;
      final messenger =
          TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
      final windowCalls = <String>[];
      messenger.setMockMethodCallHandler(
        const MethodChannel('window_manager'),
        (call) async {
          windowCalls.add(call.method);
          return switch (call.method) {
            'isMaximized' || 'isMinimized' => false,
            'getBounds' => {
              'x': 0.0,
              'y': 0.0,
              'width': 1000.0,
              'height': 750.0,
            },
            _ => null,
          };
        },
      );
      addTearDown(
        () => messenger.setMockMethodCallHandler(
          const MethodChannel('window_manager'),
          null,
        ),
      );
      final lifecycle = DesktopLifecycle(controller, isMacOS: false);
      final quitting = lifecycle.requestQuit();
      expect(controller.shutdownInProgress, true);
      expect(await controller.execute('launch'), isNull);
      await handleDesktopLifecycleCall(
        const MethodCall('openFiles', ['/tmp/late.wad']),
        controller: controller,
        onQuit: (_) async => false,
      );
      expect(backend.requests.map((request) => request.$1), ['active_jobs']);
      backend.jobsGate!.complete({'sessions': [], 'downloads': []});
      expect(await quitting, true);
      expect(controller.shutdownInProgress, true);
      expect(backend.requests.map((request) => request.$1), [
        'active_jobs',
        'shutdown',
        'update_settings',
      ]);
      expect(windowCalls.last, 'destroy');
      expect(await lifecycle.requestQuit(), false);
      controller.dispose();
    },
  );

  test(
    'expired native quit token reopens admission; accepted token keeps it closed',
    () async {
      final backend = FrontendBackend();
      final controller = AppController(backend)..snapshot = backend.state;
      final messenger =
          TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
      final lifecycleCalls = <MethodCall>[];
      var accepted = false;
      messenger.setMockMethodCallHandler(
        const MethodChannel('window_manager'),
        (call) async => switch (call.method) {
          'isMaximized' || 'isMinimized' => false,
          'getBounds' => {'x': 0.0, 'y': 0.0, 'width': 1000.0, 'height': 750.0},
          _ => throw StateError('Unexpected window call: ${call.method}'),
        },
      );
      messenger.setMockMethodCallHandler(
        const MethodChannel('doomedlauncher/lifecycle'),
        (call) async {
          lifecycleCalls.add(call);
          return accepted;
        },
      );
      addTearDown(() {
        messenger.setMockMethodCallHandler(
          const MethodChannel('window_manager'),
          null,
        );
        messenger.setMockMethodCallHandler(
          const MethodChannel('doomedlauncher/lifecycle'),
          null,
        );
      });
      final lifecycle = DesktopLifecycle(controller, isMacOS: true);
      expect(
        await lifecycle.requestQuit(
          terminateWindow: false,
          nativeRequestId: 'expired',
        ),
        false,
      );
      expect(controller.shutdownInProgress, false);
      expect(controller.error, contains('expired'));
      expect(lifecycleCalls.single.arguments, {'requestId': 'expired'});
      expect(await controller.execute('snapshot'), isNotNull);
      accepted = true;
      expect(
        await lifecycle.requestQuit(
          terminateWindow: false,
          nativeRequestId: 'current',
        ),
        true,
      );
      expect(lifecycleCalls.last.arguments, {'requestId': 'current'});
      expect(controller.shutdownInProgress, true);
      controller.dispose();
    },
  );
}
