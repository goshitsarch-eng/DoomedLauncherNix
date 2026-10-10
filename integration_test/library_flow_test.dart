import 'package:doomedlauncher/app.dart';
import 'package:doomedlauncher/bridge/backend.dart';
import 'package:doomedlauncher/state/app_controller.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/support/native_fixtures.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('desktop controls persist changes through the real Rust core', (
    tester,
  ) async {
    final fixture = await NativeFixture.create();
    final controller = AppController(fixture.backend);
    addTearDown(() async {
      controller.dispose();
      await fixture.dispose();
    });
    await tester.binding.setSurfaceSize(const Size(1280, 900));
    addTearDown(() => tester.binding.setSurfaceSize(null));

    final source = await fixture.wad(
      'Épisode 日本語.wad',
      title: 'Native adventure',
    );
    await controller.initialize();
    final imported = await controller.execute('import_files', {
      'paths': [source.path],
      'as_iwads': false,
    });
    expect(imported?['failed'], isEmpty);
    expect(controller.error, isNull);
    await tester.pumpWidget(DoomLauncherApp(controller: controller));
    await tester.pumpAndSettle();
    expect(find.text('Your library'), findsOneWidget);
    expect(find.text('Native adventure'), findsOneWidget);

    Future<void> edit(String title) async {
      await tester.tap(find.byTooltip('Actions for $title'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Edit metadata…'));
      await tester.pumpAndSettle();
      expect(find.text('Edit library entry'), findsOneWidget);
    }

    Finder field(String label) => find.byWidgetPredicate(
      (widget) => widget is TextField && widget.decoration?.labelText == label,
    );

    await edit('Native adventure');
    await tester.enterText(field('Title'), 'Saved through Flutter 日本語');
    await tester.enterText(field('Author'), 'Desktop tester 🎮');
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    expect(controller.error, isNull);
    expect(find.text('Edit library entry'), findsNothing);
    expect(find.text('Saved through Flutter 日本語'), findsOneWidget);
    final stored = (await fixture.backend.call('snapshot'))['games'] as List;
    expect(stored.single['Title'], 'Saved through Flutter 日本語');
    expect(stored.single['Author'], 'Desktop tester 🎮');

    await edit('Saved through Flutter 日本語');
    await tester.enterText(field('Title'), 'Discard this draft');
    await tester.tap(find.widgetWithText(TextButton, 'Cancel'));
    await tester.pumpAndSettle();
    expect(find.text('Saved through Flutter 日本語'), findsOneWidget);
    expect(
      ((await fixture.backend.call('snapshot'))['games'] as List)
          .single['Title'],
      'Saved through Flutter 日本語',
    );

    await tester.tap(find.text('Tags').last);
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'New tag'));
    await tester.pumpAndSettle();
    await tester.enterText(field('Tag name'), 'Desktop favorites');
    await tester.tap(
      find.widgetWithText(SwitchListTile, 'Show as library tab'),
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    expect(controller.error, isNull);
    expect(controller.tags.single['name'], 'Desktop favorites');

    await tester.tap(find.text('Library').last);
    await tester.pumpAndSettle();
    await edit('Saved through Flutter 日本語');
    final tagChip = find.widgetWithText(FilterChip, 'Desktop favorites');
    await tester.ensureVisible(tagChip);
    await tester.tap(tagChip);
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    expect(controller.games.single['tagIds'], [
      controller.tags.single['tag_id'],
    ]);

    await tester.tap(find.text('Settings').last);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('ColorThemeType:System')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Dark').last);
    await tester.pumpAndSettle();
    expect(controller.settings['ColorThemeType'], 'Dark');
    final app = tester.widget<MaterialApp>(find.byType(MaterialApp));
    expect(app.themeMode, ThemeMode.dark);

    final reopened = Backend(dataRoot: fixture.backend.dataRoot);
    await reopened.initialize();
    final persisted = await reopened.call('snapshot');
    expect(persisted['settings']['ColorThemeType'], 'Dark');
    expect((persisted['games'] as List).single['tagIds'], [
      controller.tags.single['tag_id'],
    ]);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();
  });
}
