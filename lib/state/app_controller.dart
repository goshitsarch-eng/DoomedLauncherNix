import 'dart:async';
import 'package:flutter/foundation.dart';
import '../bridge/backend.dart';
import 'diagnostics.dart';

class AppController extends ChangeNotifier {
  AppController(this.backend);
  final Backend backend;
  Map<String, dynamic> snapshot = {};
  String? error;
  int _pending = 0;
  bool _disposed = false;
  bool _shutdownInProgress = false;
  int _refreshGeneration = 0;
  bool get busy => _pending > 0 || _shutdownInProgress;
  bool get shutdownInProgress => _shutdownInProgress;

  /// Reserve shutdown before the first asynchronous check of active work.
  /// Pending admissions must finish registering their jobs before that check.
  bool beginShutdown() {
    if (_disposed || busy) return false;
    _shutdownInProgress = true;
    _notify();
    return true;
  }

  /// Reopen admission only when quitting was cancelled or failed.
  void cancelShutdown() {
    if (_disposed || !_shutdownInProgress) return;
    _shutdownInProgress = false;
    _notify();
  }

  bool _canBegin(String operation) {
    if (_disposed) return false;
    if (!_shutdownInProgress) return true;
    const failure = BackendException(
      'busy',
      'The launcher is closing. Cancel quitting before starting another action.',
    );
    error = failure.message;
    _record(operation, DiagnosticEvent.failed, failure: failure);
    _notify();
    return false;
  }

  List<Map<String, dynamic>> _rows(String key) => (snapshot[key] as List? ?? [])
      .map((e) => Map<String, dynamic>.from(e as Map))
      .toList();
  List<Map<String, dynamic>> get games => _rows('games');
  List<Map<String, dynamic>> get ports => _rows('ports');
  List<Map<String, dynamic>> get tags => _rows('tags');
  List<Map<String, dynamic>> get iwads => _rows('iwads');
  List<Map<String, dynamic>> get sessions => _rows('sessions');
  Map<String, dynamic> get settings =>
      Map<String, dynamic>.from(snapshot['settings'] as Map? ?? {});
  void _notify() {
    if (!_disposed) notifyListeners();
  }

  Future<void> initialize() async {
    if (!_canBegin('initialize')) return;
    final timer = Stopwatch()..start();
    _pending++;
    error = null;
    _notify();
    try {
      await backend.initialize();
      await refresh();
      _record('initialize', DiagnosticEvent.completed, timer: timer);
    } catch (e) {
      error = e.toString();
      _record('initialize', DiagnosticEvent.failed, timer: timer, failure: e);
    } finally {
      _pending--;
      _notify();
    }
  }

  Future<void> refresh() async {
    if (_disposed) return;
    final generation = ++_refreshGeneration;
    try {
      final next = await backend.call('snapshot');
      if (_disposed || generation != _refreshGeneration) return;
      snapshot = next;
      _notify();
    } catch (_) {
      // A newer request has already taken responsibility for current state.
      // Its success or failure is authoritative, including after disposal.
      if (_disposed || generation != _refreshGeneration) return;
      rethrow;
    }
  }

  Future<Map<String, dynamic>?> execute(
    String op, [
    Map<String, dynamic> args = const {},
  ]) async {
    if (!_canBegin(op)) return null;
    final timer = Stopwatch()..start();
    _pending++;
    error = null;
    _notify();
    try {
      final result = await backend.call(op, args);
      await refresh();
      _record(op, DiagnosticEvent.completed, timer: timer);
      return result;
    } catch (e) {
      error = e.toString();
      _record(op, DiagnosticEvent.failed, timer: timer, failure: e);
      return null;
    } finally {
      _pending--;
      _notify();
    }
  }

  void reportError(Object value) {
    final message = value.toString();
    if (error == message) return;
    error = message;
    _record('report_error', DiagnosticEvent.reported, failure: value);
    _notify();
  }

  void _record(
    String operation,
    DiagnosticEvent event, {
    Stopwatch? timer,
    Object? failure,
  }) {
    unawaited(
      backend.diagnostics.record(
        source: DiagnosticSource.flutter,
        event: event,
        operation: operation,
        durationMs: timer?.elapsedMilliseconds,
        code: failure == null
            ? null
            : failure is BackendException
            ? failure.code
            : 'unexpected',
      ),
    );
  }

  void clearError() {
    error = null;
    _notify();
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
