import 'dart:io';
import 'dart:typed_data';

import 'package:doomedlauncher/bridge/backend.dart';
import 'package:path/path.dart' as p;

/// On-disk fixtures shared by native bridge and desktop integration tests.
/// They never use the developer's configured data directory or game files.
class NativeFixture {
  NativeFixture._(this.directory, this.backend);

  final Directory directory;
  final Backend backend;

  static Future<NativeFixture> create() async {
    final directory = await Directory.systemTemp.createTemp('doom-native-');
    final backend = Backend(
      dataRoot: p.join(directory.path, 'Bibliothèque 日本語 🎮'),
    );
    try {
      await backend.initialize();
      return NativeFixture._(directory, backend);
    } catch (_) {
      await directory.delete(recursive: true);
      rethrow;
    }
  }

  Future<File> wad(
    String name, {
    String map = 'MAP07',
    String? title,
    bool iwad = false,
  }) async {
    final source = Directory(p.join(directory.path, 'Sources avec espaces'));
    await source.create(recursive: true);
    final file = File(p.join(source.path, name));
    final bytes = Uint8List(44);
    bytes.setRange(0, 4, (iwad ? 'IWAD' : 'PWAD').codeUnits);
    final header = ByteData.sublistView(bytes);
    header.setUint32(4, 2, Endian.little);
    header.setUint32(8, 12, Endian.little);
    header.setUint32(12, 12, Endian.little);
    bytes.setRange(20, 20 + map.length, map.codeUnits);
    header.setUint32(28, 12, Endian.little);
    bytes.setRange(36, 42, 'THINGS'.codeUnits);
    await file.writeAsBytes(bytes, flush: true);
    if (title != null) {
      await File(p.setExtension(file.path, '.txt')).writeAsString(
        'Title: $title\nAuthor: Élodie 日本語\n'
        'Description: Native integration fixture.\n'
        '             This text crosses the real UTF-8 bridge.\n',
      );
    }
    return file;
  }

  Future<Map<String, dynamic>> import(File file, {bool asIwad = false}) async {
    final result = await backend.call('import_files', {
      'paths': [file.path],
      'as_iwads': asIwad,
    });
    if ((result['failed'] as List).isNotEmpty) {
      throw StateError('Fixture import failed: ${result['failed']}');
    }
    final snapshot = await backend.call('snapshot');
    return Map<String, dynamic>.from(
      (snapshot['games'] as List).cast<Map>().singleWhere(
        (row) => row['FileName'] == p.basename(file.path),
      ),
    );
  }

  Future<void> dispose() async {
    if (await directory.exists()) await directory.delete(recursive: true);
  }
}
