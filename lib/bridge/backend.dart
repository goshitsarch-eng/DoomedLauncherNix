import 'dart:convert';
import 'dart:ffi';
import 'dart:io';
import 'dart:isolate';
import 'package:ffi/ffi.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';
import '../state/diagnostics.dart';

class BackendException implements Exception {
  const BackendException(this.code, this.message);
  final String code;
  final String message;
  @override
  String toString() => message;
}

/// Coarse domain operations executed outside Flutter's UI isolate.
class Backend {
  Backend({String? dataRoot, Diagnostics? diagnostics})
    : _dataRoot = dataRoot,
      diagnostics = diagnostics ?? Diagnostics(dataRoot: dataRoot);
  final Diagnostics diagnostics;
  String? _dataRoot;
  String get dataRoot =>
      _dataRoot ?? (throw StateError('Backend not initialized'));
  Future<void> initialize() async {
    _dataRoot ??= Platform.environment['DOOMEDLAUNCHER_DATA_DIR'];
    if (_dataRoot == null) {
      if (Platform.isLinux) {
        final base = Platform.environment['XDG_DATA_HOME'];
        _dataRoot = p.join(
          base != null && p.isAbsolute(base)
              ? base
              : p.join(
                  Platform.environment['HOME'] ??
                      (await getApplicationSupportDirectory()).path,
                  '.local',
                  'share',
                ),
          'doomlauncher',
        );
      } else {
        _dataRoot = (await getApplicationSupportDirectory()).path;
      }
    }
    _dataRoot = p.normalize(p.absolute(_dataRoot!));
    diagnostics.configure(_dataRoot!);
    await call('snapshot');
  }

  Future<Map<String, dynamic>> call(
    String op, [
    Map<String, dynamic> args = const {},
  ]) async {
    final timer = Stopwatch()..start();
    var source = DiagnosticSource.bridge;
    try {
      final request = jsonEncode({...args, 'op': op, 'data_root': dataRoot});
      final response = await _invokeInWorker(request);
      final envelope = jsonDecode(response) as Map<String, dynamic>;
      if (envelope['ok'] != true) {
        source = DiagnosticSource.core;
        final error = envelope['error'] as Map<String, dynamic>;
        throw BackendException(
          error['code'] as String,
          error['message'] as String,
        );
      }
      final result = Map<String, dynamic>.from(envelope['result'] as Map);
      await diagnostics.record(
        source: DiagnosticSource.bridge,
        event: DiagnosticEvent.completed,
        operation: op,
        durationMs: timer.elapsedMilliseconds,
      );
      return result;
    } catch (error) {
      await diagnostics.record(
        source: source,
        event: DiagnosticEvent.failed,
        operation: op,
        code: error is BackendException ? error.code : 'unexpected',
        durationMs: timer.elapsedMilliseconds,
      );
      rethrow;
    }
  }
}

// Keep the worker closure's capture limited to the request bytes. In
// particular, diagnostic sinks and pending file operations stay on Dart's
// application isolate.
Future<String> _invokeInWorker(String request) =>
    Isolate.run(() => invokeNative(request));

typedef _InvokeNative = Pointer<Utf8> Function(Pointer<Uint8>, IntPtr);
typedef _InvokeDart = Pointer<Utf8> Function(Pointer<Uint8>, int);
typedef _FreeNative = Void Function(Pointer<Utf8>);
typedef _FreeDart = void Function(Pointer<Utf8>);

String invokeNative(String request) {
  final bytes = utf8.encode(request);
  if (bytes.length > 8 * 1024 * 1024) {
    throw const BackendException(
      'validation',
      'This operation contains too much data. Import fewer files at once.',
    );
  }
  final library = _loadLibrary();
  final abi = library.lookupFunction<Uint32 Function(), int Function()>(
    'dl_abi_version',
  )();
  if (abi != 1) {
    throw const BackendException(
      'native_library',
      'The application and core library versions do not match.',
    );
  }
  final invoke = library.lookupFunction<_InvokeNative, _InvokeDart>(
    'dl_invoke',
  );
  final release = library.lookupFunction<_FreeNative, _FreeDart>('dl_free');
  final input = malloc<Uint8>(bytes.length);
  Pointer<Utf8> output = nullptr;
  try {
    input.asTypedList(bytes.length).setAll(0, bytes);
    output = invoke(input, bytes.length);
    if (output == nullptr) {
      throw const BackendException(
        'internal',
        'The core could not allocate a response.',
      );
    }
    return output.toDartString();
  } finally {
    malloc.free(input);
    if (output != nullptr) release(output);
  }
}

DynamicLibrary _loadLibrary() {
  final override = Platform.environment['DOOMEDLAUNCHER_CORE_LIBRARY'];
  final executableDir = p.dirname(Platform.resolvedExecutable);
  final fileName = Platform.isWindows
      ? 'doomedlauncher_core.dll'
      : Platform.isMacOS
      ? 'libdoomedlauncher_core.dylib'
      : 'libdoomedlauncher_core.so';
  final candidates = <String>[
    ?override,
    if (Platform.isMacOS) p.join(executableDir, '..', 'Frameworks', fileName),
    p.join(executableDir, 'lib', fileName),
    p.join(executableDir, fileName),
    // Supported local Flutter tests use cargo's debug output.
    p.join(Directory.current.path, 'rust', 'target', 'debug', fileName),
    p.join(Directory.current.path, 'rust', 'target', 'release', fileName),
  ];
  for (final path in candidates) {
    if (!File(path).existsSync()) continue;
    try {
      return DynamicLibrary.open(path);
    } catch (error) {
      throw BackendException(
        'native_library',
        'Cannot load the application core at $path: $error',
      );
    }
  }
  throw const BackendException(
    'native_library',
    'The application core library is missing. Run the documented native build or reinstall the application.',
  );
}
