import 'dart:convert';
import 'dart:io';
import 'package:path/path.dart' as p;

enum DiagnosticSource { flutter, bridge, core, platform }

enum DiagnosticEvent { completed, failed, reported }

/// The schema intentionally has no message, request, arguments, URL, or path
/// field. UI error messages may contain private user data and stay in the UI.
class Diagnostics {
  Diagnostics({
    this._dataRoot,
    this.maxBytes = 256 * 1024,
    this.maxFiles = 3,
    void Function(String)? fallback,
  }) : _fallback = fallback ?? stderr.writeln {
    if (maxBytes < 512 || maxFiles < 1 || maxFiles > 10) {
      throw ArgumentError(
        'Use at least 512 bytes and between one and ten log files.',
      );
    }
  }

  final int maxBytes;
  final int maxFiles;
  final void Function(String) _fallback;
  String? _dataRoot;
  Future<void> _pending = Future.value();

  static const _operations = {
    'initialize',
    'report_error',
    'snapshot',
    'active_jobs',
    'shutdown',
    'import_files',
    'import_directory',
    'edit_entry',
    'rename_entry',
    'delete_entry',
    'resync_metadata',
    'entry_details',
    'open_text',
    'import_associations',
    'delete_association',
    'save_tag',
    'delete_tag',
    'save_port',
    'delete_port',
    'detect_ports',
    'update_settings',
    'play_defaults',
    'launch_preview',
    'launch',
    'sessions',
    'stop_session',
    'scan_stores',
    'search_idgames',
    'start_download',
    'download_status',
    'cancel_download',
    'discard_download',
  };
  static const _codes = {
    'validation',
    'not_found',
    'permission',
    'io',
    'database',
    'invalid_data',
    'corrupt_data',
    'network',
    'limit',
    'cancelled',
    'busy',
    'recovery',
    'runtime',
    'process',
    'host_access',
    'image',
    'internal',
    'native_library',
    'unsupported',
    'unexpected',
    'unknown',
  };

  void configure(String dataRoot) {
    _dataRoot = dataRoot;
  }

  Future<void> record({
    required DiagnosticSource source,
    required DiagnosticEvent event,
    required String operation,
    String? code,
    int? durationMs,
  }) {
    final record = <String, Object>{
      'time': DateTime.now().toUtc().toIso8601String(),
      'source': source.name,
      'event': event.name,
      'operation': _operations.contains(operation) ? operation : 'unknown',
      if (code != null) 'code': _codes.contains(code) ? code : 'unknown',
      if (durationMs != null) 'duration_ms': durationMs < 0 ? 0 : durationMs,
    };
    final root = _dataRoot;
    // Serialization gives concurrent operations whole JSONL records and keeps
    // rotation coordinated within this application instance.
    _pending = _pending.then((_) => _write(root, record));
    return _pending;
  }

  Future<void> flush() => _pending;

  Future<void> _write(String? root, Map<String, Object> record) async {
    try {
      if (root == null || root.isEmpty) {
        _writeFallback(record, 'no library directory');
        return;
      }
      final directory = Directory(p.join(root, 'Logs'));
      final type = await FileSystemEntity.type(
        directory.path,
        followLinks: false,
      );
      if (type != FileSystemEntityType.notFound &&
          type != FileSystemEntityType.directory) {
        throw const FileSystemException(
          'The diagnostic directory is not a directory.',
        );
      }
      await directory.create(recursive: true);
      final current = File(p.join(directory.path, 'application.jsonl'));
      final bytes = utf8.encode('${jsonEncode(record)}\n');
      // A bounded metadata record normally fits well below the minimum budget.
      if (bytes.length > maxBytes) {
        _writeFallback(record, 'record exceeds log budget');
        return;
      }
      await _checkRegularFile(current);
      if (await current.exists()) {
        final length = await current.length();
        if (length > maxBytes) {
          await current.delete();
        } else if (length + bytes.length > maxBytes) {
          await _rotate(directory);
        }
      }
      await current.writeAsBytes(bytes, mode: FileMode.append);
    } catch (_) {
      // Do not recursively log sink failures or expose a filesystem error that
      // might contain a username, private library path, or network mount.
      _writeFallback(record, 'local log unavailable');
    }
  }

  Future<void> _checkRegularFile(File file) async {
    final type = await FileSystemEntity.type(file.path, followLinks: false);
    if (type != FileSystemEntityType.notFound &&
        type != FileSystemEntityType.file) {
      throw const FileSystemException(
        'Diagnostic files must be regular files.',
      );
    }
  }

  Future<void> _rotate(Directory directory) async {
    for (var index = maxFiles - 1; index >= 0; index--) {
      final path = p.join(
        directory.path,
        index == 0 ? 'application.jsonl' : 'application.$index.jsonl',
      );
      final file = File(path);
      await _checkRegularFile(file);
      if (!await file.exists()) continue;
      if (index == maxFiles - 1) {
        await file.delete();
      } else {
        await file.rename(
          p.join(directory.path, 'application.${index + 1}.jsonl'),
        );
      }
    }
  }

  void _writeFallback(Map<String, Object> record, String reason) {
    try {
      _fallback('Doom Launcher diagnostics: $reason; ${jsonEncode(record)}');
    } catch (_) {
      // Both diagnostic destinations are unavailable. Application operations
      // must still complete; there is no third sink to retry recursively.
      return;
    }
  }
}
