import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:doomedlauncher/app.dart';
import 'package:doomedlauncher/bridge/backend.dart';
import 'package:doomedlauncher/state/app_controller.dart';
import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:window_manager/window_manager.dart';

import '../test/support/native_fixtures.dart';

/// Runs on the real desktop runner and native Rust core, never a fake backend.
/// Optional DOOMEDLAUNCHER_QA_EVIDENCE captures the actual X11 display on Linux.
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  WidgetController.hitTestWarningShouldBeFatal = true;
  // TestWidgetsFlutterBinding suppresses license registration. Load the actual
  // desktop bundle's notices so its real license list can be exercised too.
  LicenseRegistry.addLicense(() async* {
    final bytes = await rootBundle.load('NOTICES.Z');
    final notices = utf8.decode(gzip.decode(bytes.buffer.asUint8List()));
    for (final notice in notices.split('\n${'-' * 80}\n')) {
      final split = notice.indexOf('\n\n');
      yield LicenseEntryWithLineBreaks(
        split < 0 ? const [] : notice.substring(0, split).split('\n'),
        split < 0 ? notice : notice.substring(split + 2),
      );
    }
  });

  Future<({NativeFixture fixture, AppController controller})> mount(
    WidgetTester tester,
  ) async {
    final fixture = await NativeFixture.create();
    final controller = AppController(fixture.backend);
    await controller.initialize();
    await windowManager.ensureInitialized();
    await windowManager.setSize(const Size(1200, 900));
    await tester.pumpWidget(DoomLauncherApp(controller: controller));
    await tester.pumpAndSettle();
    addTearDown(() async {
      if (Platform.isLinux) {
        await Process.run('xdotool', ['key', '--clearmodifiers', 'Escape']);
        await Future<void>.delayed(const Duration(milliseconds: 200));
      }
      await tester.pumpWidget(const SizedBox.shrink());
      controller.dispose();
      await fixture.dispose();
    });
    return (fixture: fixture, controller: controller);
  }

  Finder field(String label) => find.byWidgetPredicate(
    (widget) => widget is TextField && widget.decoration?.labelText == label,
  );

  Future<void> tap(WidgetTester tester, Finder finder) async {
    expect(finder, findsOneWidget);
    final action = finder.describeMatch(Plurality.one);
    debugPrint(
      'AUDIT ACTION: ${action.length > 180 ? '${action.substring(0, 180)}…' : action}',
    );
    await tester.ensureVisible(finder);
    await tester.pumpAndSettle();
    await tester.tap(finder);
    await tester.pumpAndSettle();
    await tester.pump(const Duration(milliseconds: 100));
    await tester.pumpAndSettle();
  }

  Future<void> enter(WidgetTester tester, Finder finder, String value) async {
    await tester.ensureVisible(finder);
    await tester.pumpAndSettle();
    await tester.tap(finder);
    await tester.pumpAndSettle();
    await tester.enterText(finder, value);
    await tester.pumpAndSettle();
    expect(tester.widget<TextField>(finder).controller!.text, value);
  }

  Future<void> text(WidgetTester tester, String label) async {
    for (var i = 0; i < 30 && find.text(label).evaluate().isEmpty; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    if (find.text(label).evaluate().isEmpty) {
      final evidence = Platform.environment['DOOMEDLAUNCHER_QA_EVIDENCE'];
      if (evidence != null && Platform.isLinux) {
        await Process.run('import', [
          '-window',
          'root',
          '$evidence/missing-${label.replaceAll(RegExp(r'[^a-zA-Z0-9]'), '-')}.png',
        ]);
      }
    }
    await tap(tester, find.text(label).last);
  }

  Future<void> page(WidgetTester tester, String label) => tap(
    tester,
    find.descendant(
      of: find.byType(NavigationRail),
      matching: find.text(label),
    ),
  );

  Future<void> menu(WidgetTester tester, String name, String item) async {
    await tap(tester, find.widgetWithText(SubmenuButton, name));
    await tap(tester, find.widgetWithText(MenuItemButton, item));
  }

  Future<void> checkpoint(WidgetTester tester, String name) async {
    await tester.pumpAndSettle();
    final evidence = Platform.environment['DOOMEDLAUNCHER_QA_EVIDENCE'];
    if (evidence != null && Platform.isLinux) {
      await Directory(evidence).create(recursive: true);
      final result = await Process.run('import', [
        '-window',
        'root',
        '$evidence/$name.png',
      ]);
      expect(result.exitCode, 0, reason: '${result.stderr}');
    }
    expect(tester.takeException(), isNull, reason: name);
    debugPrint('AUDIT PASS: $name');
  }

  Future<void> nativeChoice(WidgetTester tester, [String? path]) async {
    Future<String> active() async =>
        '${(await Process.run('xdotool', ['getactivewindow', 'getwindowname'])).stdout}'
            .trim();
    for (var i = 0; i < 20 && await active() == 'Doom Launcher'; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(await active(), anyOf('Open File', 'Choose Directory'));
    await Future<void>.delayed(const Duration(milliseconds: 300));
    if (path == null) {
      await Process.run('xdotool', ['key', '--clearmodifiers', 'Escape']);
    } else {
      // Leave GTK's Recent view before using its location entry.
      await Process.run('xdotool', ['key', '--clearmodifiers', 'alt+Home']);
      await Future<void>.delayed(const Duration(milliseconds: 200));
      await Process.run('xdotool', [
        'key',
        '--clearmodifiers',
        'ctrl+l',
        'ctrl+a',
      ]);
      await Process.run('xdotool', [
        'type',
        '--clearmodifiers',
        '--delay',
        '2',
        path,
      ]);
      for (var i = 0; i < 5 && await active() != 'Doom Launcher'; i++) {
        await Process.run('xdotool', ['key', '--clearmodifiers', 'Return']);
        await Future<void>.delayed(const Duration(milliseconds: 700));
        await tester.pump();
      }
      if (await active() == 'Choose Directory') {
        // GTK can navigate into the typed folder without accepting it.
        // Activate its actual confirmation button after navigation settles.
        final geometry =
            '${(await Process.run('xdotool', ['getactivewindow', 'getwindowgeometry', '--shell'])).stdout}';
        int dimension(String name) =>
            int.parse(RegExp('$name=([0-9]+)').firstMatch(geometry)!.group(1)!);
        final window =
            '${(await Process.run('xdotool', ['getactivewindow'])).stdout}'
                .trim();
        await Process.run('xdotool', [
          'mousemove',
          '--window',
          window,
          '${dimension('WIDTH') - 65}',
          '${dimension('HEIGHT') - 28}',
          'click',
          '1',
        ]);
        await Future<void>.delayed(const Duration(milliseconds: 700));
        await tester.pump();
      }
    }
    await tester.pumpAndSettle();
    expect(await active(), 'Doom Launcher');
  }

  testWidgets('real desktop empty-state menus, navigation and resizing', (
    tester,
  ) async {
    await mount(tester);
    expect(find.text('Set up your first game'), findsOneWidget);
    for (final section in ['Library', 'Source ports', 'Tags', 'Settings']) {
      await menu(tester, 'View', section);
      await checkpoint(tester, 'empty-view-${section.replaceAll(' ', '-')}');
    }
    await menu(tester, 'Help', 'Keyboard shortcuts');
    expect(find.textContaining('Ctrl/Cmd+O'), findsOneWidget);
    await text(tester, 'Close');
    for (final entry in [('File', 'About Doom Launcher'), ('Help', 'About')]) {
      await menu(tester, entry.$1, entry.$2);
      expect(find.byType(AboutDialog), findsOneWidget);
      await windowManager.setSize(const Size(760, 560));
      await tester.pumpAndSettle();
      await text(tester, 'View licenses');
      expect(find.byType(LicensePage), findsOneWidget);
      for (var i = 0; i < 50 && find.byType(ListTile).evaluate().isEmpty; i++) {
        await tester.pump(const Duration(milliseconds: 100));
        await Future<void>.delayed(const Duration(milliseconds: 100));
      }
      await tap(tester, find.byType(ListTile).first);
      await checkpoint(tester, 'license-detail');
      await tester.pageBack();
      await tester.pumpAndSettle();
      await tester.pageBack();
      await tester.pumpAndSettle();
      await text(tester, 'Close');
      await windowManager.setSize(const Size(1200, 900));
      await tester.pumpAndSettle();
    }
    await menu(tester, 'Library', 'Refresh library');
    for (final size in [
      const Size(760, 560),
      const Size(1800, 560),
      const Size(760, 1100),
      const Size(1200, 900),
    ]) {
      await windowManager.setSize(size);
      await tester.pumpAndSettle();
      for (final section in ['Library', 'Source ports', 'Tags', 'Settings']) {
        await page(tester, section);
        await checkpoint(
          tester,
          'empty-${section.replaceAll(' ', '-')}-${size.width.toInt()}x${size.height.toInt()}',
        );
      }
    }
    await page(tester, 'Library');
    await tap(tester, find.widgetWithText(SubmenuButton, 'Library'));
    for (final label in [
      'Play selected',
      'Play with options…',
      'Edit selected…',
      'Delete selected…',
    ]) {
      final button = tester.widget<MenuItemButton>(
        find.widgetWithText(MenuItemButton, label),
      );
      expect(
        button.onPressed,
        isNull,
        reason: '$label must not pretend to act with no selection',
      );
    }
    await tester.sendKeyEvent(LogicalKeyboardKey.escape);
    await tester.pumpAndSettle();
  });

  testWidgets('real desktop tags, source ports and settings persist edits', (
    tester,
  ) async {
    final app = await mount(tester);
    await page(tester, 'Tags');
    await text(tester, 'New tag');
    await text(tester, 'Save');
    expect(find.text('Enter a tag name.'), findsOneWidget);
    await enter(tester, field('Tag name'), '  Épisode 日本語 🎮  ');
    await text(tester, 'Show as library tab');
    await text(tester, 'Save');
    expect(app.controller.tags.single['name'], 'Épisode 日本語 🎮');
    await tap(tester, find.byTooltip('Rename Épisode 日本語 🎮'));
    await enter(tester, field('Tag name'), 'Discarded draft');
    await text(tester, 'Cancel');
    expect(app.controller.tags.single['name'], 'Épisode 日本語 🎮');
    await tap(tester, find.byTooltip('Show as library tab'));
    await tap(tester, find.byTooltip('Show as library tab'));
    await tap(tester, find.byTooltip('Delete Épisode 日本語 🎮'));
    await text(tester, 'Cancel');
    await checkpoint(tester, 'tags-edited');

    await page(tester, 'Source ports');
    await text(tester, 'Add source port');
    await text(tester, 'Save');
    expect(find.text('Name is required.'), findsOneWidget);
    await enter(tester, field('Name'), 'QA utility');
    await enter(tester, field('Executable'), '/usr/bin/true');
    await text(tester, 'Doom source port');
    await text(tester, 'Utility');
    await enter(tester, field('Working directory'), '/tmp');
    await enter(tester, field('Supported extensions'), '.wad,.pk3');
    await enter(tester, field('File parameter'), '');
    await enter(tester, field('Extra parameters'), '');
    await enter(tester, field('Save game directory'), '');
    await text(tester, 'Save');
    expect(app.controller.ports.single['name'], 'QA utility');
    await tap(tester, find.byTooltip('Edit QA utility'));
    await enter(tester, field('Name'), 'Discarded port');
    await text(tester, 'Cancel');
    await tap(tester, find.byTooltip('Remove QA utility'));
    await text(tester, 'Cancel');

    await page(tester, 'Settings');
    Future<void> choose(String key, String option) async {
      await tap(
        tester,
        find.byKey(ValueKey('$key:${app.controller.settings[key]}')),
      );
      await text(tester, option);
    }

    for (final mode in ['Dark', 'Light', 'Follow system']) {
      await choose('ColorThemeType', mode);
      await checkpoint(tester, 'theme-${mode.replaceAll(' ', '-')}');
    }
    await choose('GameFileViewType', 'Tiles');
    await choose('GameFileViewType', 'List');
    for (final label in [
      'Show the play dialog before launching',
      'Extract title pictures when importing',
    ]) {
      await text(tester, label);
      await text(tester, label);
    }
    for (final label in ['Recent', 'Untagged', 'IWADs', 'idgames']) {
      final chip = find.widgetWithText(FilterChip, label);
      await tap(tester, chip);
      await tap(tester, chip);
    }
    await tester.ensureVisible(field('Download mirror URL'));
    await enter(tester, field('Download mirror URL'), 'not a URL');
    await text(tester, 'Save download settings');
    expect(
      find.text('Enter an HTTP or HTTPS URL without credentials.'),
      findsOneWidget,
    );
    await enter(
      tester,
      field('Download mirror URL'),
      'https://example.org/idgames/',
    );
    await enter(tester, field('idgames website URL'), 'https://example.org/');
    await enter(tester, field('API path'), '../escape');
    await text(tester, 'Save download settings');
    expect(find.textContaining('Enter a relative API path'), findsOneWidget);
    await enter(tester, field('API path'), 'api/api.php');
    await text(tester, 'Save download settings');
    await text(tester, 'Advanced');
    expect(find.textContaining('Library data:'), findsOneWidget);
    await checkpoint(tester, 'settings-network-and-advanced');
    await text(tester, 'Advanced');
    final reopened = Backend(dataRoot: app.fixture.backend.dataRoot);
    await reopened.initialize();
    final snapshot = await reopened.call('snapshot');
    expect(snapshot['settings']['MirrorUrl'], 'https://example.org/idgames/');
    expect((snapshot['tags'] as List).single['name'], 'Épisode 日本語 🎮');
    expect((snapshot['ports'] as List).single['name'], 'QA utility');
  });

  testWidgets('real library sorting, actions, metadata and detail refresh', (
    tester,
  ) async {
    final app = await mount(tester);
    for (final name in ['Alpha', 'Beta', 'Gamma']) {
      await app.fixture.import(await app.fixture.wad('$name.wad', title: name));
    }
    await app.controller.refresh();
    await tester.pumpAndSettle();
    for (final label in ['Recent', 'Untagged', 'IWADs', 'All mods']) {
      await tap(tester, find.widgetWithText(ChoiceChip, label));
    }
    for (final label in [
      'Title',
      'Author',
      'Filename',
      'Rating',
      'Last played',
      'Imported',
      'Play time',
    ]) {
      await tap(tester, find.byTooltip('Sort library'));
      await tap(
        tester,
        find.widgetWithText(CheckedPopupMenuItem<String>, label),
      );
    }
    await tap(tester, find.byTooltip('Ascending order'));
    await tap(tester, find.byTooltip('Descending order'));
    await tap(tester, find.byTooltip('Show tile view'));
    await checkpoint(tester, 'library-tiles');
    await tap(tester, find.byTooltip('Show list view'));
    final search = find.byWidgetPredicate(
      (widget) =>
          widget is TextField &&
          widget.decoration?.hintText == 'Search title, author or filename',
    );
    await enter(tester, search, 'no matching 日本語');
    expect(find.text('0 shown'), findsOneWidget);
    await tap(tester, find.byTooltip('Clear search'));
    await enter(tester, search, 'Alpha');
    await text(tester, 'Alpha');
    expect(tester.widget<TextField>(search).focusNode!.hasFocus, isFalse);
    for (final shortcut in [LogicalKeyboardKey.keyE, LogicalKeyboardKey.keyP]) {
      await tester.sendKeyDownEvent(LogicalKeyboardKey.controlLeft);
      await tester.sendKeyEvent(shortcut);
      await tester.sendKeyUpEvent(LogicalKeyboardKey.controlLeft);
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsOneWidget);
      await text(tester, 'Cancel');
    }
    for (final label in [
      'Play selected',
      'Play with options…',
      'Edit selected…',
      'Delete selected…',
    ]) {
      await menu(tester, 'Library', label);
      expect(find.byType(AlertDialog), findsOneWidget);
      await text(tester, 'Cancel');
    }
    await tester.sendKeyEvent(LogicalKeyboardKey.f5);
    await tester.pumpAndSettle();
    await tap(tester, find.byTooltip('Clear search'));
    await tester.tap(find.text('Alpha').first);
    await tester.pump(const Duration(milliseconds: 60));
    await tester.tap(find.text('Alpha').first);
    await tester.pumpAndSettle();
    expect(find.byType(AlertDialog), findsOneWidget);
    await text(tester, 'Cancel');
    await tester.tap(find.text('Alpha').first, buttons: 2);
    await tester.pumpAndSettle();
    await text(tester, 'Details & statistics');
    await tap(tester, find.byTooltip('Close'));
    await tap(tester, find.byTooltip('Actions for Alpha'));
    await text(tester, 'Edit metadata…');
    for (final entry in {
      'Title': 'Alpha edited',
      'Author': 'Élodie 日本語',
      'Description': 'First line\nSecond line 🎮',
      'Comments': 'Notes with punctuation: <>&"',
      'Release date': '2026-10-09',
    }.entries) {
      await tester.ensureVisible(field(entry.key));
      await enter(tester, field(entry.key), entry.value);
    }
    for (final rating in [
      '1 / 5',
      '2 / 5',
      '3 / 5',
      '4 / 5',
      '5 / 5',
      'Unrated',
    ]) {
      await tap(
        tester,
        find.widgetWithText(DropdownButtonFormField<int>, 'Your rating'),
      );
      await text(tester, rating);
    }
    await text(tester, 'Save');
    expect(
      app.controller.games.firstWhere(
        (g) => g['FileName'] == 'Alpha.wad',
      )['Title'],
      'Alpha edited',
    );
    await tap(tester, find.byTooltip('Actions for Alpha edited'));
    await text(tester, 'Details & statistics');
    await text(tester, 'Edit');
    await enter(tester, field('Title'), 'Changed inside details');
    await text(tester, 'Save');
    expect(
      find.text('Changed inside details'),
      findsWidgets,
      reason: 'The still-open details dialog must refresh after saving',
    );
    final detailDialog = find.byType(Dialog).last;
    expect(
      find.descendant(
        of: detailDialog,
        matching: find.text('Changed inside details'),
      ),
      findsOneWidget,
    );
    await checkpoint(tester, 'details-after-edit');
    await tap(tester, find.byTooltip('Close'));
    await tap(tester, find.byTooltip('Actions for Changed inside details'));
    await text(tester, 'Rename file…');
    for (final empty in ['', '   ']) {
      await enter(tester, field('Filename including extension'), empty);
      expect(
        tester
            .widget<FilledButton>(find.widgetWithText(FilledButton, 'Rename'))
            .onPressed,
        isNull,
      );
    }
    await enter(tester, field('Filename including extension'), '../escape.wad');
    await text(tester, 'Rename');
    expect(find.text('Rename file'), findsOneWidget);
    await enter(tester, field('Filename including extension'), 'Beta.wad');
    await text(tester, 'Rename');
    expect(find.text('Rename file'), findsOneWidget);
    await enter(
      tester,
      field('Filename including extension'),
      'Épisode 日本語.wad',
    );
    await text(tester, 'Rename');
    expect(find.text('Rename file'), findsNothing);
    expect(
      app.controller.games.any((g) => g['FileName'] == 'Épisode 日本語.wad'),
      isTrue,
    );
    app.controller.clearError();
    await tester.pumpAndSettle();
    await tap(tester, find.byTooltip('Actions for Beta'));
    await text(tester, 'Delete…');
    await text(tester, 'Cancel');
    await tap(tester, find.byTooltip('Actions for Beta'));
    await text(tester, 'Delete…');
    await text(tester, 'Also delete the managed game file');
    await text(tester, 'Delete');
    expect(
      app.controller.games.any((g) => g['FileName'] == 'Beta.wad'),
      isFalse,
    );
    final gamma = app.controller.games.firstWhere(
      (g) => g['FileName'] == 'Gamma.wad',
    );
    await tap(tester, find.byTooltip('Actions for Gamma'));
    await text(tester, 'Delete…');
    await text(tester, 'Delete');
    expect(await File('${gamma['path']}').exists(), isTrue);
    await checkpoint(tester, 'library-after-delete');
  });

  testWidgets('real idgames HTTP search fields and download lifecycle', (
    tester,
  ) async {
    final app = await mount(tester);
    final payload = await (await app.fixture.wad('network.wad')).readAsBytes();
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    addTearDown(() => server.close(force: true));
    final requests = <Uri>[];
    var failDownload = false;
    var slowDownload = false;
    final delayedResponses = <Completer<void>>[];
    var corruptDownload = false;
    var failSearch = false;
    var networkTitle = 'Network audit map';
    var networkFilename = 'network.wad';
    final origin = 'http://127.0.0.1:${server.port}/';
    final serving = server.listen((request) async {
      requests.add(request.uri);
      if (request.uri.path.endsWith('api.php')) {
        if (failSearch) {
          request.response.statusCode = 503;
          await request.response.close();
          return;
        }
        request.response.headers.contentType = ContentType.json;
        request.response.write(
          jsonEncode({
            'content': {
              'file': [
                {
                  'id': 1,
                  'filename': networkFilename,
                  'title': networkTitle,
                  'author': 'Élodie 日本語',
                  'date': '2026-10-09',
                  'dir': 'levels/',
                  'url': '${origin}details',
                  'description': 'First line<br>Second line',
                  'size': payload.length,
                  'rating': 4.5,
                },
              ],
            },
          }),
        );
      } else {
        if (slowDownload) {
          final gate = Completer<void>();
          delayedResponses.add(gate);
          await gate.future;
        }
        request.response.statusCode = failDownload ? 503 : 200;
        request.response.add(corruptDownload ? [1, 2, 3] : payload);
      }
      await request.response.close();
    });
    addTearDown(() {
      for (final gate in delayedResponses) {
        if (!gate.isCompleted) gate.complete();
      }
      return serving.cancel();
    });
    await app.controller.execute('update_settings', {
      'values': {
        'MirrorUrl': origin,
        'IdGamesUrl': origin,
        'ApiPage': 'api.php',
      },
    });
    await menu(tester, 'View', 'Get mods');
    expect(find.text('Network audit map'), findsOneWidget);
    await text(tester, 'Search');
    expect(find.textContaining('Enter a title, author'), findsOneWidget);
    await enter(tester, field('Search idgames'), 'Élodie 日本語');
    for (final choice in [
      ('Title', 'title'),
      ('Author', 'author'),
      ('Filename', 'filename'),
      ('Description', 'description'),
    ]) {
      final dropdown = find.widgetWithText(
        DropdownButtonFormField<String>,
        'Search by',
      );
      await tap(tester, dropdown);
      await text(tester, choice.$1);
      await text(tester, 'Search');
      expect(
        find.textContaining('Search failed:'),
        findsNothing,
        reason: '${choice.$1} must be accepted across the Rust bridge',
      );
      expect(requests.last.queryParameters['type'], choice.$2);
      expect(requests.last.queryParameters['query'], 'Élodie 日本語');
    }
    failSearch = true;
    await text(tester, 'Search');
    expect(find.textContaining('Search failed:'), findsOneWidget);
    failSearch = false;
    await text(tester, 'Latest');
    expect(requests.last.queryParameters['action'], 'latestfiles');
    await text(tester, 'Network audit map');
    expect(find.text('Author: Élodie 日本語'), findsOneWidget);
    await windowManager.setSize(const Size(760, 560));
    await checkpoint(tester, 'network-details-minimum');
    await text(tester, 'Website');
    await text(tester, 'Close');
    await tap(tester, find.byTooltip('View website'));
    await checkpoint(tester, 'network-page-minimum');
    await windowManager.setSize(const Size(1200, 900));
    failDownload = true;
    await tap(tester, find.byTooltip('Download and import'));
    for (
      var i = 0;
      i < 30 && find.textContaining('Download failed').evaluate().isEmpty;
      i++
    ) {
      await tester.pump(const Duration(milliseconds: 200));
    }
    expect(find.textContaining('Download failed'), findsOneWidget);
    failDownload = false;
    slowDownload = true;
    await tester.tap(find.text('Retry'));
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.text('Cancel'), findsOneWidget);
    final cancelButton = find
        .ancestor(
          of: find.text('Cancel'),
          matching: find.byWidgetPredicate((w) => w is TextButton),
        )
        .first;
    // Native job allocation is asynchronous; Cancel is disabled until ready.
    for (
      var i = 0;
      i < 100 && tester.widget<TextButton>(cancelButton).onPressed == null;
      i++
    ) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(tester.widget<TextButton>(cancelButton).onPressed, isNotNull);
    await tester.tap(find.text('Cancel'));
    for (
      var i = 0;
      i < 100 && find.text('Download cancelled.').evaluate().isEmpty;
      i++
    ) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(find.text('Download cancelled.'), findsOneWidget);
    slowDownload = false;
    for (final gate in delayedResponses) {
      if (!gate.isCompleted) gate.complete();
    }
    await text(tester, 'Retry');
    for (var i = 0; i < 40 && app.controller.games.isEmpty; i++) {
      await tester.pump(const Duration(milliseconds: 200));
    }
    await tester.pumpAndSettle();
    expect(app.controller.games.single['FileName'], 'network.wad');
    expect(find.text('Imported 1 file into your library.'), findsOneWidget);
    await checkpoint(tester, 'network-downloaded-and-imported');
    await text(tester, 'Play');
    expect(find.text('Add a source port before launching.'), findsOneWidget);
    await text(tester, 'Cancel');
    await page(tester, 'Library');
    expect(find.byTooltip('Actions for network.wad'), findsOneWidget);
    await page(tester, 'Get mods');
    expect(find.text('Imported 1 file into your library.'), findsOneWidget);
    corruptDownload = true;
    await text(tester, 'Network audit map');
    await text(tester, 'Download');
    for (
      var i = 0;
      i < 40 && find.text('Retry import').evaluate().isEmpty;
      i++
    ) {
      await tester.pump(const Duration(milliseconds: 200));
    }
    await text(tester, 'Retry import');
    await text(tester, 'Discard download');
    expect(find.text('Retry import'), findsNothing);
    corruptDownload = false;
    await tap(tester, find.byTooltip('Download and play'));
    for (
      var i = 0;
      i < 40 && find.byType(AlertDialog).evaluate().isEmpty;
      i++
    ) {
      await tester.pump(const Duration(milliseconds: 200));
    }
    expect(app.controller.games, hasLength(1));
    await text(tester, 'Cancel');
    networkFilename = 'network-play.wad';
    await text(tester, 'Latest');
    await tap(tester, find.byTooltip('Download and play'));
    for (
      var i = 0;
      i < 40 && find.byType(AlertDialog).evaluate().isEmpty;
      i++
    ) {
      await tester.pump(const Duration(milliseconds: 200));
    }
    await text(tester, 'Cancel');
    await checkpoint(tester, 'network-retry-discard-duplicate-play');
    networkTitle = List.filled(80, 'Long network title 日本語').join(' ');
    await text(tester, 'Latest');
    await windowManager.setSize(const Size(760, 560));
    await tester.pumpAndSettle();
    await text(tester, networkTitle);
    await checkpoint(tester, 'long-network-details-minimum');
    await text(tester, 'Author: Élodie 日本語');
    await checkpoint(tester, 'long-network-details-scrolled');
    await text(tester, 'Close');
  });

  testWidgets('real launch dialog options, ordering and owned process', (
    tester,
  ) async {
    final app = await mount(tester);
    await app.fixture.import(
      await app.fixture.wad('base.wad', iwad: true),
      asIwad: true,
    );
    for (final name in ['Launch map', 'One', 'Two']) {
      await app.fixture.import(await app.fixture.wad('$name.wad', title: name));
    }
    final engine = File('${app.fixture.directory.path}/qa-engine.sh');
    await engine.writeAsString(
      '#!/bin/sh\nprintf "%s\\n" "\$@" > "${app.fixture.directory.path}/argv.txt"\nsleep 120\n',
    );
    await Process.run('chmod', ['+x', engine.path]);
    await app.controller.execute('save_port', {
      'port': {
        'name': 'QA engine',
        'executable': engine.path,
        'directory': '',
        'file_option': '-file',
        'supported_extensions': '.wad',
        'extra_parameters': '',
        'alt_save_directory': '',
        'launch_type': 0,
      },
    });
    addTearDown(() => app.fixture.backend.call('shutdown'));
    expect(app.controller.error, isNull);
    await tester.pumpAndSettle();
    await text(tester, 'Launch map');
    await text(tester, 'Options');
    await text(tester, 'Start at a specific map');
    await tap(tester, find.byTooltip('Choose a map'));
    await text(tester, 'MAP07');
    for (final skill in [
      "1 — I'm too young to die",
      '2 — Hey, not too rough',
      '3 — Hurt me plenty',
      '4 — Ultra-Violence',
      '5 — Nightmare!',
    ]) {
      await tap(
        tester,
        find.widgetWithText(DropdownButtonFormField<int>, 'Skill'),
      );
      await text(tester, skill);
    }
    await enter(tester, field('Extra parameters'), '"unterminated');
    await tester.pump(const Duration(milliseconds: 300));
    await tester.pumpAndSettle();
    expect(find.textContaining('unmatched double quote'), findsWidgets);
    await enter(tester, field('Extra parameters'), '-nosound');
    for (final label in [
      'Use extra parameters only',
      'Load latest save',
      'Save play statistics',
      'Record demo',
      'Remember these settings',
    ]) {
      await text(tester, label);
      await text(tester, label);
    }
    await tap(tester, find.byTooltip('Add file'));
    await tap(tester, find.byTooltip('Add file'));
    await tap(tester, find.byTooltip('Move up').last);
    await tap(tester, find.byTooltip('Move down').first);
    await tap(tester, find.byTooltip('Remove file').last);
    await checkpoint(tester, 'launch-options-and-ordered-mods');
    await text(tester, 'Launch');
    expect(
      app.controller.sessions.any((s) => s['status'] == 'running'),
      isTrue,
    );
    final argv = await File(
      '${app.fixture.directory.path}/argv.txt',
    ).readAsString();
    expect(argv, contains('-nosound'));
    expect(argv, contains('-warp\n7'));
    await tap(tester, find.byTooltip('Stop game'));
    await text(tester, 'Cancel');
    await tap(tester, find.byTooltip('Stop game'));
    await text(tester, 'Stop');
    for (
      var i = 0;
      i < 100 && app.controller.sessions.any((s) => s['status'] == 'running');
      i++
    ) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    expect(
      app.controller.sessions.any((s) => s['status'] == 'running'),
      isFalse,
    );
    await text(tester, 'Options');
    expect(
      tester.widget<TextField>(field('Extra parameters')).controller!.text,
      '-nosound',
    );
    await text(tester, 'Cancel');
    await checkpoint(tester, 'owned-process-stopped-and-settings-restored');
  });

  testWidgets('long user content and repeated dialogs at minimum size', (
    tester,
  ) async {
    final app = await mount(tester);
    final longName = List.filled(4, 'Épisode 日本語 long name').join(' ');
    final longTitle = List.filled(12, longName).join(' ');
    final game = await app.fixture.import(
      await app.fixture.wad('long.wad', title: longTitle),
    );
    await app.controller.execute('save_tag', {
      'name': longName,
      'has_tab': true,
    });
    await app.controller.execute('save_port', {
      'port': {
        'name': longName,
        'executable': '/usr/bin/true',
        'directory': '',
        'file_option': '',
        'supported_extensions': '.wad',
        'extra_parameters': '',
        'alt_save_directory': '',
        'launch_type': 1,
      },
    });
    expect(app.controller.error, isNull);
    await windowManager.setSize(const Size(760, 560));
    await tester.pumpAndSettle();
    await page(tester, 'Tags');
    await checkpoint(tester, 'long-tag-minimum');
    await tap(tester, find.byTooltip('Rename $longName'));
    await checkpoint(tester, 'long-tag-dialog-minimum');
    await text(tester, 'Cancel');
    await page(tester, 'Source ports');
    await checkpoint(tester, 'long-port-minimum');
    await tap(tester, find.byTooltip('Edit $longName'));
    await checkpoint(tester, 'long-port-dialog-minimum');
    await text(tester, 'Cancel');
    await page(tester, 'Library');
    await tap(tester, find.byTooltip('Actions for $longTitle'));
    await text(tester, 'Play with options…');
    await checkpoint(tester, 'long-play-dialog-minimum');
    await text(tester, 'Cancel');
    await text(tester, longTitle);
    await menu(tester, 'Library', 'Delete selected…');
    expect(find.byType(AlertDialog), findsOneWidget);
    await checkpoint(tester, 'long-delete-dialog-minimum');
    await text(tester, 'Also delete the managed game file');
    await text(tester, 'Also delete the managed game file');
    await checkpoint(tester, 'long-delete-dialog-scrolled');
    await text(tester, 'Cancel');
    for (var i = 0; i < 3; i++) {
      await tap(tester, find.byTooltip('Actions for $longTitle'));
      await text(tester, 'Edit metadata…');
      await enter(tester, field('Title'), i.isEven ? '' : '  ');
      await tester.sendKeyEvent(LogicalKeyboardKey.escape);
      await tester.pumpAndSettle();
      expect(app.controller.games.single['Title'], game['Title']);
    }
    await checkpoint(tester, 'cancelled-metadata-and-escape');
  });

  testWidgets('native pickers, associated files and store scan controls', (
    tester,
  ) async {
    final app = await mount(tester);
    for (final label in [
      'Import files…',
      'Import base games (IWADs)…',
      'Import directory…',
    ]) {
      await menu(tester, 'File', label);
      await nativeChoice(tester);
      expect(app.controller.games, isEmpty);
    }
    final source = await app.fixture.wad('Picker.wad', title: 'Picker entry');
    await menu(tester, 'File', 'Import files…');
    await nativeChoice(tester, source.path);
    expect(app.controller.games.single['Title'], 'Picker entry');
    await tap(tester, find.byTooltip('Import options'));
    await text(tester, 'Import files…');
    await nativeChoice(tester);
    await tap(tester, find.byTooltip('Import options'));
    await text(tester, 'Import directory…');
    await nativeChoice(tester);
    await text(tester, 'Import');
    await nativeChoice(tester);

    await tap(tester, find.byTooltip('Actions for Picker entry'));
    await text(tester, 'Copy file path');
    expect(
      (await Clipboard.getData(Clipboard.kTextPlain))!.text,
      app.controller.games.single['path'],
    );
    await tap(tester, find.byTooltip('Actions for Picker entry'));
    await text(tester, 'Resync metadata');
    expect(app.controller.error, isNull);
    await tap(tester, find.byTooltip('Actions for Picker entry'));
    await text(tester, 'Details & statistics');
    final image = await File(
      'data/com.goshapps.DoomLauncher.png',
    ).copy('${app.fixture.directory.path}/screenshot.png');
    await text(tester, 'Import screenshot, save or demo');
    await nativeChoice(tester, image.path);
    expect(find.text('screenshot.png'), findsOneWidget);
    await tap(tester, find.byTooltip('Remove'));
    await text(tester, 'Cancel');
    await tap(tester, find.byTooltip('Remove'));
    await text(tester, 'Remove');
    expect(find.text('screenshot.png'), findsNothing);
    await checkpoint(tester, 'association-import-and-remove');
    await tap(tester, find.byTooltip('Close'));
    await tap(tester, find.byTooltip('Actions for Picker entry'));
    await text(tester, 'Use as a base game (IWAD)');
    expect(app.controller.iwads, hasLength(1));
    await tap(tester, find.widgetWithText(ChoiceChip, 'IWADs'));
    expect(find.text('Picker entry'), findsOneWidget);

    await page(tester, 'Source ports');
    await text(tester, 'Add source port');
    for (final label in [
      'Browse Executable',
      'Browse Working directory',
      'Browse Save game directory',
    ]) {
      await tap(tester, find.byTooltip(label));
      await nativeChoice(tester);
      await tap(tester, find.byTooltip(label));
      await nativeChoice(
        tester,
        label == 'Browse Executable' ? '/usr/bin/true' : '/tmp/',
      );
    }
    expect(
      tester.widget<TextField>(field('Executable')).controller!.text,
      '/usr/bin/true',
    );
    await text(tester, 'Cancel');
    await text(tester, 'Detect installed ports');
    expect(app.controller.error, isNull);
    await checkpoint(tester, 'detected-native-ports');

    final base = await app.fixture.wad('DOOM2.WAD', iwad: true);
    await File(
      '${base.parent.path}/doom64_x64',
    ).writeAsString('#!/bin/sh\nexit 0\n');
    await menu(tester, 'File', 'Scan Steam / GOG / Heroic / Lutris…');
    await text(tester, 'Scan again');
    await text(tester, 'Choose folder');
    await nativeChoice(tester);
    await text(tester, 'Choose folder');
    await nativeChoice(tester, '${base.parent.path}/');
    expect(find.text('DOOM2.WAD'), findsOneWidget);
    final selectAll = find.byWidgetPredicate(
      (widget) =>
          widget is CheckboxListTile &&
          (widget.title as Text?)?.data?.startsWith('Select all') == true,
    );
    await tap(tester, selectAll);
    await tap(tester, selectAll);
    await tap(tester, find.widgetWithText(CheckboxListTile, 'DOOM2.WAD'));
    await tap(tester, find.widgetWithText(CheckboxListTile, 'DOOM2.WAD'));
    await text(tester, 'Add Doom 64 as a source port');
    await text(tester, 'Add Doom 64 as a source port');
    await windowManager.setSize(const Size(760, 560));
    await checkpoint(tester, 'store-scan-minimum');
    await windowManager.setSize(const Size(1200, 900));
    await checkpoint(tester, 'store-scan-selected');
    await text(tester, 'Import selected');
    expect(
      app.controller.games.any((g) => g['FileName'] == 'DOOM2.WAD'),
      isTrue,
    );
    expect(app.controller.ports.any((p) => p['name'] == 'Doom 64'), isTrue);
    await menu(tester, 'File', 'Scan Steam / GOG / Heroic / Lutris…');
    await text(tester, 'Choose folder');
    await nativeChoice(tester, '${base.parent.path}/');
    await tap(
      tester,
      find.descendant(
        of: find.byType(InputChip),
        matching: find.byTooltip('Delete'),
      ),
    );
    await text(tester, 'Close');
  });
  testWidgets(
    'tag assignments, defaults, missing files and keyboard controls',
    (tester) async {
      final app = await mount(tester);
      final game = await app.fixture.import(
        await app.fixture.wad('controls.wad', title: 'Controls'),
      );
      await app.fixture.import(
        await app.fixture.wad('BASE.WAD', iwad: true),
        asIwad: true,
      );
      await app.controller.refresh();
      await tester.pumpAndSettle();
      await page(tester, 'Tags');
      await text(tester, 'New tag');
      await enter(tester, field('Tag name'), '   ');
      await text(tester, 'Save');
      expect(find.text('Enter a tag name.'), findsOneWidget);
      await enter(tester, field('Tag name'), 'Keyboard tag');
      await text(tester, 'Show as library tab');
      await text(tester, 'Save');
      await text(tester, 'New tag');
      await enter(tester, field('Tag name'), 'Keyboard tag');
      await text(tester, 'Save');
      expect(find.text('New tag'), findsWidgets);
      await text(tester, 'Cancel');
      await page(tester, 'Library');
      await tap(tester, find.byTooltip('Actions for Controls'));
      await text(tester, 'Edit metadata…');
      await tap(tester, find.widgetWithText(FilterChip, 'Keyboard tag'));
      await text(tester, 'Save');
      await tap(tester, find.widgetWithText(ChoiceChip, 'Keyboard tag'));
      expect(find.text('Controls'), findsOneWidget);
      await page(tester, 'Tags');
      await text(tester, 'Keyboard tag');
      await enter(tester, field('Tag name'), 'Renamed tag');
      await text(tester, 'Save');
      await page(tester, 'Library');
      expect(find.widgetWithText(ChoiceChip, 'Renamed tag'), findsOneWidget);
      await page(tester, 'Tags');
      await tap(tester, find.byTooltip('Delete Renamed tag'));
      await text(tester, 'Remove');
      await page(tester, 'Library');
      expect(find.text('Controls'), findsOneWidget);
      await page(tester, 'Source ports');
      await text(tester, 'Add source port');
      await enter(tester, field('Name'), 'Missing executable');
      await enter(tester, field('Executable'), '/not/a/real/engine');
      await text(tester, 'Doom source port');
      await text(tester, 'Doom 64');
      await text(tester, 'Doom 64');
      await text(tester, 'Doom source port');
      await text(tester, 'Save');
      await text(tester, 'Missing executable');
      await text(tester, 'Cancel');
      await page(tester, 'Settings');
      for (final item in [
        ('Default source port', 'Missing executable'),
        ('Default IWAD', 'BASE'),
      ]) {
        await tap(
          tester,
          find.widgetWithText(DropdownButtonFormField<String>, item.$1),
        );
        await text(tester, item.$2);
        await tap(
          tester,
          find.widgetWithText(DropdownButtonFormField<String>, item.$1),
        );
        await text(tester, 'Choose automatically');
        await tap(
          tester,
          find.widgetWithText(DropdownButtonFormField<String>, item.$1),
        );
        await text(tester, item.$2);
      }
      await enter(
        tester,
        field('Download mirror URL'),
        'https://example.org/unsaved/',
      );
      await app.controller.refresh();
      await tester.pumpAndSettle();
      expect(
        tester.widget<TextField>(field('Download mirror URL')).controller!.text,
        'https://example.org/unsaved/',
      );
      await page(tester, 'Library');
      await tap(tester, find.byTooltip('Actions for Controls'));
      await text(tester, 'Play with options…');
      await text(tester, 'Launch');
      expect(find.byType(AlertDialog), findsOneWidget);
      expect(find.textContaining('not/a/real/engine'), findsWidgets);
      await text(tester, 'Cancel');
      await page(tester, 'Source ports');
      await tap(tester, find.byTooltip('Remove Missing executable'));
      await text(tester, 'Remove');
      expect(app.controller.ports, isEmpty);
      await text(tester, 'Add source port');
      await enter(tester, field('Name'), 'Standalone utility');
      await enter(tester, field('Executable'), '/usr/bin/true');
      await text(tester, 'Doom source port');
      await text(tester, 'Utility');
      await text(tester, 'Save');
      await page(tester, 'Library');
      await tap(tester, find.byTooltip('Actions for Controls'));
      await text(tester, 'Play with options…');
      expect(find.textContaining('This utility starts'), findsOneWidget);
      await text(tester, 'Launch');
      for (var i = 0; i < 30; i++) {
        await app.controller.refresh();
        await tester.pump(const Duration(milliseconds: 100));
        if (app.controller.sessions.any((s) => s['status'] == 'completed')) {
          break;
        }
      }
      expect(
        app.controller.sessions.any((s) => s['status'] == 'completed'),
        isTrue,
      );
      await page(tester, 'Source ports');
      await tap(tester, find.byTooltip('Edit Standalone utility'));
      await enter(tester, field('Executable'), '/usr/bin/false');
      await text(tester, 'Save');
      await page(tester, 'Library');
      await tap(tester, find.byTooltip('Actions for Controls'));
      await text(tester, 'Play with options…');
      await text(tester, 'Launch');
      for (var i = 0; i < 30; i++) {
        await app.controller.refresh();
        await tester.pump(const Duration(milliseconds: 100));
        if (app.controller.sessions.any((s) => s['status'] == 'failed')) break;
      }
      final failed = app.controller.sessions.singleWhere(
        (s) => s['status'] == 'failed',
      );
      await tap(tester, find.byTooltip('${failed['error']}'));
      // The launch notification may precede this message in the snackbar queue.
      for (
        var i = 0;
        i < 100 && find.text('${failed['error']}').evaluate().isEmpty;
        i++
      ) {
        await tester.pump(const Duration(milliseconds: 100));
      }
      expect(find.text('${failed['error']}'), findsWidgets);
      await checkpoint(tester, 'failed-process-details');
      await page(tester, 'Source ports');
      await tap(tester, find.byTooltip('Remove Standalone utility'));
      await text(tester, 'Remove');
      await File('${game['path']}').delete();
      await app.controller.refresh();
      await page(tester, 'Library');
      await tap(tester, find.byTooltip('Actions for Controls'));
      for (final label in [
        'Play',
        'Play with options…',
        'Rename file…',
        'Resync metadata',
        'Open text file',
        'Open file externally',
      ]) {
        expect(
          tester
              .widget<PopupMenuItem<String>>(
                find.widgetWithText(PopupMenuItem<String>, label),
              )
              .enabled,
          isFalse,
        );
      }
      await tester.sendKeyEvent(LogicalKeyboardKey.escape);
      await tester.pumpAndSettle();
      await checkpoint(tester, 'missing-file-actions-disabled');
      await text(tester, 'Controls');
      await tap(tester, find.widgetWithText(SubmenuButton, 'Library'));
      for (final label in ['Play selected', 'Play with options…']) {
        expect(
          tester
              .widget<MenuItemButton>(
                find.widgetWithText(MenuItemButton, label),
              )
              .onPressed,
          isNull,
        );
      }
      await tester.sendKeyEvent(LogicalKeyboardKey.escape);
      await tester.pumpAndSettle();
    },
  );
  testWidgets('setup actions, keyboard traversal, large library and geometry', (
    tester,
  ) async {
    final app = await mount(tester);
    await text(tester, '1. Add a source port');
    expect(find.text('Add source port'), findsOneWidget);
    await page(tester, 'Library');
    await text(tester, '2. Add base games (IWADs)');
    await nativeChoice(tester);
    await text(tester, 'Find installed games');
    await text(tester, 'Close');
    await page(tester, 'Tags');
    await text(tester, 'New tag');
    await enter(tester, field('Tag name'), 'Keyboard only');
    await tester.sendKeyEvent(LogicalKeyboardKey.tab);
    await tester.sendKeyEvent(LogicalKeyboardKey.space);
    await tester.pumpAndSettle();
    expect(
      tester.widget<SwitchListTile>(find.byType(SwitchListTile)).value,
      isTrue,
    );
    await tester.sendKeyDownEvent(LogicalKeyboardKey.shiftLeft);
    await tester.sendKeyEvent(LogicalKeyboardKey.tab);
    await tester.sendKeyUpEvent(LogicalKeyboardKey.shiftLeft);
    await tester.sendKeyEvent(LogicalKeyboardKey.backspace);
    await tester.sendKeyEvent(LogicalKeyboardKey.delete);
    await tester.pumpAndSettle();
    await tester.sendKeyEvent(LogicalKeyboardKey.escape);
    await tester.pumpAndSettle();
    expect(app.controller.tags, isEmpty);
    final watch = Stopwatch()..start();
    for (var i = 0; i < 250; i++) {
      await app.fixture.import(
        await app.fixture.wad(
          'map-${i.toString().padLeft(3, '0')}.wad',
          title: 'Map $i',
        ),
      );
    }
    await app.controller.refresh();
    await page(tester, 'Library');
    debugPrint(
      'AUDIT OBSERVATION: 250-entry fixture import and first UI render ${watch.elapsedMilliseconds} ms',
    );
    for (final layout in ['Show tile view', 'Show list view']) {
      await tap(tester, find.byTooltip(layout));
      await tester.drag(
        find.byType(layout == 'Show tile view' ? GridView : ListView).last,
        const Offset(0, -600),
      );
      await tester.pumpAndSettle();
      await checkpoint(tester, 'large-library-$layout');
    }
    final search = find.byWidgetPredicate(
      (w) =>
          w is TextField &&
          w.decoration?.hintText == 'Search title, author or filename',
    );
    await enter(tester, search, 'Map 249');
    await text(tester, 'Map 249');
    await tap(tester, find.byTooltip('Details'));
    await tap(tester, find.byTooltip('Details'));
    for (final size in [
      const Size(760, 560),
      const Size(1800, 560),
      const Size(760, 1100),
      const Size(1200, 900),
    ]) {
      await windowManager.setSize(size);
      await tester.pumpAndSettle();
      await tap(tester, find.byTooltip('Actions for Map 249'));
      await text(tester, 'Details & statistics');
      await checkpoint(
        tester,
        'populated-details-${size.width.toInt()}x${size.height.toInt()}',
      );
      await tap(tester, find.byTooltip('Close'));
    }
    await windowManager.maximize();
    await checkpoint(tester, 'maximized-library');
    await windowManager.unmaximize();
    await checkpoint(tester, 'restored-library');
    for (var i = 0; i < 3; i++) {
      for (final section in ['Tags', 'Settings', 'Source ports', 'Library']) {
        await page(tester, section);
      }
    }
    await checkpoint(tester, 'repeated-navigation');
  });
}
