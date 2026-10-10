import 'dart:convert';
import 'dart:io';

import 'package:doomedlauncher/bridge/backend.dart';
import 'package:doomedlauncher/main.dart' show DesktopLifecycle;
import 'package:doomedlauncher/state/app_controller.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;

import 'support/native_fixtures.dart';

// Requires a built Rust library. `cargo build --manifest-path rust/Cargo.toml`
// supplies the default local location; DOOMEDLAUNCHER_CORE_LIBRARY overrides it.
void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  late NativeFixture fixture;

  setUp(() async => fixture = await NativeFixture.create());
  tearDown(() async => fixture.dispose());

  test(
    'failed native startup can quit without writing window preferences',
    () async {
      final root = Directory(p.join(fixture.directory.path, 'corrupt-startup'));
      await root.create();
      final database = File(p.join(root.path, 'DoomLauncher.sqlite'));
      await database.writeAsString('not a sqlite database');
      final controller = AppController(Backend(dataRoot: root.path));
      addTearDown(controller.dispose);
      await controller.initialize();
      expect(controller.snapshot, isEmpty);
      expect(controller.error, contains('not a database'));
      expect(
        await DesktopLifecycle(
          controller,
          isMacOS: false,
        ).requestQuit(terminateWindow: false),
        isTrue,
      );
      expect(await database.readAsString(), 'not a sqlite database');
    },
  );

  test(
    'native UTF-8 import, rename, metadata and tags persist across backends',
    () async {
      final source = await fixture.wad(
        'Épisode 日本語.wad',
        title: 'Bibliothèque 🚀',
      );
      final game = await fixture.import(source);
      final id = game['GameFileID'];
      expect(game['Title'], 'Bibliothèque 🚀');
      expect(game['Author'], 'Élodie 日本語');
      expect(game['Map'], 'MAP07');
      expect(game['MapCount'], 1);

      await fixture.backend.call('save_tag', {
        'name': 'Favoris 日本語',
        'has_tab': true,
      });
      final tag =
          ((await fixture.backend.call('snapshot'))['tags'] as List).single;
      await fixture.backend.call('edit_entry', {
        'id': id,
        'fields': {
          'Title': 'Renamed title 🎮',
          'Comments': 'Quotes " and apostrophes \'\nLine two 日本語',
          'Rating': 5,
        },
        'tag_ids': [tag['tag_id']],
      });
      await fixture.backend.call('rename_entry', {
        'id': id,
        'name': 'renommé 日本語.wad',
      });
      await fixture.backend.call('update_settings', {
        'values': {'ColorThemeType': 'Dark', 'ShowPlayDialog': 'false'},
      });

      final reopened = Backend(dataRoot: fixture.backend.dataRoot);
      await reopened.initialize();
      final snapshot = await reopened.call('snapshot');
      final stored = (snapshot['games'] as List).single;
      expect(stored['GameFileID'], id);
      expect(stored['FileName'], 'renommé 日本語.wad');
      expect(stored['Title'], 'Renamed title 🎮');
      expect(stored['Rating'], 5);
      expect(stored['Comments'], contains('Line two 日本語'));
      expect(stored['tagIds'], [tag['tag_id']]);
      expect(snapshot['settings']['ColorThemeType'], 'Dark');
      expect(snapshot['settings']['ShowPlayDialog'], 'false');
      expect(
        await File(
          p.join(reopened.dataRoot, 'GameFiles', 'renommé 日本語.wad'),
        ).readAsBytes(),
        await source.readAsBytes(),
      );
      expect(await source.exists(), isTrue);
    },
  );

  test(
    'invalid native requests recover and never publish a corrupt import',
    () async {
      final corrupt = File(p.join(fixture.directory.path, 'broken.wad'));
      await corrupt.writeAsString('PWADbroken');
      final imported = await fixture.backend.call('import_files', {
        'paths': [corrupt.path],
      });
      expect(imported['added'], isEmpty);
      expect(imported['failed'], hasLength(1));
      expect(
        await File(
          p.join(fixture.backend.dataRoot, 'GameFiles', 'broken.wad'),
        ).exists(),
        isFalse,
      );
      expect((await fixture.backend.call('snapshot'))['games'], isEmpty);

      final game = await fixture.import(await fixture.wad('valid.wad'));
      for (final request in [
        ('rename_entry', {'id': game['GameFileID'], 'name': '../escape.wad'}),
        (
          'edit_entry',
          {
            'id': game['GameFileID'],
            'fields': {'Rating': 99},
          },
        ),
        (
          'update_settings',
          {
            'values': {'ColorThemeType': 'invalid'},
          },
        ),
        ('not_an_operation', <String, dynamic>{}),
      ]) {
        await expectLater(
          fixture.backend.call(request.$1, request.$2),
          throwsA(isA<BackendException>()),
        );
        expect((await fixture.backend.call('snapshot'))['games'], hasLength(1));
      }
      // This also exercises native response ownership on malformed JSON, before
      // a Request can be deserialized or the library database opened.
      final envelope = jsonDecode(invokeNative('{invalid JSON')) as Map;
      expect(envelope['ok'], isFalse);
      expect(envelope['error']['code'], 'invalid_data');
      final details = await fixture.backend.call('entry_details', {
        'id': game['GameFileID'],
      });
      expect(details['game']['FileName'], 'valid.wad');
    },
  );

  test(
    'concurrent native isolates preserve every committed operation',
    () async {
      await Future.wait(
        List.generate(12, (index) {
          return fixture.backend.call('save_tag', {
            'name': 'Parallel tag $index 日本語',
            'has_tab': index.isEven,
          });
        }),
      );
      final snapshots = await Future.wait(
        List.generate(8, (_) => fixture.backend.call('snapshot')),
      );
      for (final snapshot in snapshots) {
        expect(snapshot['tags'], hasLength(12));
        expect(
          (snapshot['tags'] as List).map((tag) => tag['tag_id']).toSet(),
          hasLength(12),
        );
      }
    },
  );

  test(
    'controller surfaces native validation and recovers on the next action',
    () async {
      final controller = AppController(fixture.backend);
      addTearDown(controller.dispose);
      await controller.initialize();
      expect(controller.error, isNull);
      expect(controller.busy, isFalse);
      expect(
        await controller.execute('save_tag', {'name': '', 'has_tab': false}),
        isNull,
      );
      expect(controller.error, contains('Tag names'));
      expect(controller.busy, isFalse);
      await controller.execute('save_tag', {
        'name': 'Recovered',
        'has_tab': true,
      });
      expect(controller.error, isNull);
      expect(controller.tags.single['name'], 'Recovered');
    },
  );

  test(
    'loose sidecars follow rename/delete while shared text is preserved',
    () async {
      final first = await fixture.import(
        await fixture.wad('shared.wad', title: 'Shared readme'),
      );
      final second = await fixture.import(
        await fixture.wad('shared.deh', title: 'Shared readme'),
      );
      final managed = p.join(fixture.backend.dataRoot, 'GameFiles');
      final oldText = File(p.join(managed, 'shared.txt'));
      expect(await oldText.exists(), isTrue);
      await fixture.backend.call('rename_entry', {
        'id': first['GameFileID'],
        'name': 'renamed.wad',
      });
      final movedText = File(p.join(managed, 'renamed.txt'));
      expect(
        await oldText.exists(),
        isTrue,
        reason: 'The second entry still uses the original text.',
      );
      expect(await movedText.readAsString(), await oldText.readAsString());
      final opened = await fixture.backend.call('open_text', {
        'id': first['GameFileID'],
      });
      expect(opened['path'], movedText.path);
      await fixture.backend.call('delete_entry', {
        'id': second['GameFileID'],
        'delete_file': true,
      });
      expect(await oldText.exists(), isFalse);
      expect(await movedText.exists(), isTrue);
      await fixture.backend.call('delete_entry', {
        'id': first['GameFileID'],
        'delete_file': true,
      });
      expect(await movedText.exists(), isFalse);
      expect((await fixture.backend.call('snapshot'))['games'], isEmpty);
      final retained = await Directory(
        p.join(fixture.backend.dataRoot, 'Trash'),
      ).list(recursive: true).toList();
      expect(
        retained.whereType<File>().where((f) => f.path.endsWith('renamed.txt')),
        hasLength(1),
      );
    },
  );

  test(
    'sidecar collisions leave both the original game and existing text intact',
    () async {
      final game = await fixture.import(
        await fixture.wad('original.wad', title: 'Original readme'),
      );
      final managed = p.join(fixture.backend.dataRoot, 'GameFiles');
      final conflict = File(p.join(managed, 'collision.txt'));
      await conflict.writeAsString('Existing unrelated text');
      await expectLater(
        fixture.backend.call('rename_entry', {
          'id': game['GameFileID'],
          'name': 'collision.wad',
        }),
        throwsA(isA<BackendException>()),
      );
      expect(await conflict.readAsString(), 'Existing unrelated text');
      expect(await File(p.join(managed, 'original.wad')).exists(), isTrue);
      expect(await File(p.join(managed, 'original.txt')).exists(), isTrue);
      expect(await File(p.join(managed, 'collision.wad')).exists(), isFalse);
      final opened = await fixture.backend.call('open_text', {
        'id': game['GameFileID'],
      });
      expect(
        await File(opened['path'] as String).readAsString(),
        contains('Original readme'),
      );
    },
  );

  test(
    'native launch preserves argv, completes a real process and records it',
    () async {
      final engine = File(p.join(fixture.directory.path, 'engine with spaces'));
      await engine.writeAsString(
        '#!/bin/sh\n'
        'printf \'%s\\n\' "\$@" > "\$0.args"\n'
        'printf \'native fixture completed\\n\'\n'
        'exit 0\n',
        flush: true,
      );
      final chmod = await Process.run('chmod', ['+x', engine.path]);
      expect(chmod.exitCode, 0, reason: '${chmod.stderr}');
      final iwad = await fixture.import(
        await fixture.wad('doom2.wad', iwad: true),
        asIwad: true,
      );
      final game = await fixture.import(await fixture.wad('addon 日本語.wad'));
      await fixture.backend.call('save_port', {
        'port': {
          'name': 'Native test engine',
          'executable': engine.path,
          'directory': engine.parent.path,
          'supported_extensions': 'wad,deh,bex',
        },
      });
      final snapshot = await fixture.backend.call('snapshot');
      final port = (snapshot['ports'] as List).single;
      final iwadId = (snapshot['iwads'] as List).singleWhere(
        (row) => row['game_file_id'] == iwad['GameFileID'],
      )['iwad_id'];
      final options = {
        'game_file_id': game['GameFileID'],
        'source_port_id': port['id'],
        'iwad_id': iwadId,
        'map': 'MAP07',
        'skill': 4,
        'extra_params': '-name "Player with spaces"',
        'save_statistics': false,
        'remember': true,
      };
      final plan = await fixture.backend.call('launch_preview', {
        'options': options,
      });
      expect(plan['program'], engine.path);
      expect(plan['arguments'], contains('Player with spaces'));
      expect(plan['arguments'], contains('-iwad'));
      expect(plan['arguments'], contains('-file'));
      final started = await fixture.backend.call('launch', {
        'options': options,
      });
      final sessionId = started['session']['id'];
      Map<dynamic, dynamic>? completed;
      final deadline = DateTime.now().add(const Duration(seconds: 10));
      while (DateTime.now().isBefore(deadline)) {
        final sessions =
            (await fixture.backend.call('sessions'))['sessions'] as List;
        final session = sessions.cast<Map>().singleWhere(
          (s) => s['id'] == sessionId,
        );
        if (session['finished_at'] != null) {
          completed = session;
          break;
        }
        await Future<void>.delayed(const Duration(milliseconds: 50));
      }
      expect(
        completed,
        isNotNull,
        reason: 'The fixture engine did not finish.',
      );
      expect(completed!['status'], 'completed');
      expect(completed['exit_code'], 0);
      expect(
        await File('${engine.path}.args').readAsLines(),
        plan['arguments'],
      );
      expect(
        await File(completed['log_path'] as String).readAsString(),
        contains('native fixture completed'),
      );
      final after = await fixture.backend.call('entry_details', {
        'id': game['GameFileID'],
      });
      expect(after['game']['LastPlayed'], isNotEmpty);
      final remembered = await fixture.backend.call('play_defaults', {
        'id': game['GameFileID'],
      });
      expect(remembered['options']['extra_params'], options['extra_params']);
      expect(remembered['options']['source_port_id'], port['id']);
    },
    skip: !Platform.isLinux && !Platform.isMacOS,
  );
}
