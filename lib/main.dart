import 'dart:async';
import 'dart:io';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:window_manager/window_manager.dart';
import 'app.dart';
import 'bridge/backend.dart';
import 'state/app_controller.dart';

Future<void> main(List<String> arguments) async {
  if (arguments.contains('--version') || arguments.contains('-v')) {
    stdout.writeln('Doom Launcher 5.0.0-alpha.1');
    return;
  }
  if (arguments.contains('--help') || arguments.contains('-h')) {
    stdout.writeln(
      'Doom Launcher\nUsage: doomedlauncher [--help] [--version] [FILE ...]\n\nImport positional files into your library and open the launcher.',
    );
    return;
  }
  WidgetsFlutterBinding.ensureInitialized();
  final controller = AppController(Backend());
  await controller.initialize();
  final finderFiles = <String>[];
  DesktopLifecycle? desktopLifecycle;
  if (Platform.isMacOS) {
    const lifecycleChannel = MethodChannel('doomedlauncher/lifecycle');
    lifecycleChannel.setMethodCallHandler(
      (call) => handleDesktopLifecycleCall(
        call,
        controller: controller,
        onQuit: (requestId) async =>
            await desktopLifecycle?.requestQuit(
              terminateWindow: false,
              nativeRequestId: requestId,
            ) ??
            false,
      ),
    );

    try {
      finderFiles.addAll(
        await lifecycleChannel.invokeListMethod<String>('ready') ?? [],
      );
    } catch (error) {
      controller.reportError(error);
    }
  }
  await windowManager.ensureInitialized();
  final width = (double.tryParse('${controller.settings['AppWidth']}') ?? 1200)
      .clamp(760.0, 2560.0);
  final height = (double.tryParse('${controller.settings['AppHeight']}') ?? 800)
      .clamp(560.0, 1600.0);
  const minimum = Size(760, 560);
  final lifecycle = DesktopLifecycle(controller);
  desktopLifecycle = lifecycle;
  windowManager.addListener(lifecycle);
  await windowManager.waitUntilReadyToShow(
    WindowOptions(
      size: Size(width, height),
      minimumSize: minimum,
      center: true,
      title: 'Doom Launcher',
    ),
    () async {
      await windowManager.setPreventClose(true);
      await windowManager.show();
      await windowManager.focus();
    },
  );
  runApp(DoomLauncherApp(controller: controller));
  final files = [
    ...arguments.where((argument) => !argument.startsWith('-')),
    ...finderFiles,
  ].map((argument) => File(argument).absolute.path).toSet().toList();
  if (files.isNotEmpty) {
    unawaited(importStartupFiles(controller, files));
  }
}

class DesktopLifecycle with WindowListener {
  DesktopLifecycle(this.controller, {bool? isMacOS})
    : _isMacOS = isMacOS ?? Platform.isMacOS;
  final bool _isMacOS;
  final AppController controller;
  Timer? _resizeDebounce;
  bool _closing = false;
  Future<void> _saveGeometry({bool closing = false}) async {
    if (controller.shutdownInProgress && !closing) return;
    if (await windowManager.isMaximized() ||
        await windowManager.isMinimized()) {
      return;
    }
    final size = await windowManager.getSize();
    final values = {
      'values': {
        'AppWidth': '${size.width.round()}',
        'AppHeight': '${size.height.round()}',
      },
    };
    if (closing) {
      await controller.backend.call('update_settings', values);
    } else {
      await controller.execute('update_settings', values);
    }
  }

  @override
  void onWindowResize() {
    _resizeDebounce?.cancel();
    _resizeDebounce = Timer(const Duration(milliseconds: 750), _saveGeometry);
  }

  @override
  void onWindowClose() async {
    await requestQuit();
  }

  Future<bool> requestQuit({
    bool terminateWindow = true,
    String? nativeRequestId,
  }) async {
    if (_closing || controller.shutdownInProgress) return false;
    if (!controller.beginShutdown()) {
      controller.reportError(
        'Wait for the current library operation to finish before closing.',
      );
      return false;
    }
    _closing = true;
    _resizeDebounce?.cancel();
    var announcedShutdown = false;
    var approved = false;
    try {
      if (controller.snapshot.isNotEmpty &&
          !await prepareLauncherShutdown(
            controller,
            context: launcherNavigatorKey.currentContext,
          )) {
        return false;
      }
      await _saveGeometry(closing: true);
      await controller.backend.diagnostics.flush();
      if (_isMacOS) {
        final accepted = await const MethodChannel('doomedlauncher/lifecycle')
            .invokeMethod<bool>(
              'shutdownReady',
              terminateWindow ? null : {'requestId': nativeRequestId},
            );
        if (accepted != true) {
          throw StateError(
            'The system quit request expired. Please quit again.',
          );
        }
        announcedShutdown = true;
      }
      if (terminateWindow) {
        await windowManager.destroy();
        windowManager.removeListener(this);
      }
      approved = true;
      return true;
    } catch (error) {
      if (announcedShutdown) {
        try {
          await const MethodChannel(
            'doomedlauncher/lifecycle',
          ).invokeMethod<void>('shutdownCancelled');
        } catch (_) {
          // Preserve the original shutdown failure.
        }
      }
      controller.reportError(error);
      return false;
    } finally {
      _closing = false;
      if (!approved) controller.cancelShutdown();
    }
  }
}

/// Cancels only application-owned work, and keeps the window open on failure.
Future<bool> prepareLauncherShutdown(
  AppController controller, {
  BuildContext? context,
}) async {
  try {
    final jobs = await controller.backend.call('active_jobs');
    final sessions = (jobs['sessions'] as List? ?? []).length;
    final downloads = (jobs['downloads'] as List? ?? []).length;
    if (sessions > 0 || downloads > 0) {
      if (context == null || !context.mounted) return false;
      final confirmed = await showDialog<bool>(
        context: context,
        barrierDismissible: false,
        builder: (context) => AlertDialog(
          title: const Text('Stop active work and quit?'),
          content: Text(
            '$sessions running game sessions and $downloads downloads are active. Quitting will stop them. Save your game progress before continuing.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Stop and quit'),
            ),
          ],
        ),
      );
      if (confirmed != true) return false;
    }
    await controller.backend.call('shutdown');
    return true;
  } catch (error) {
    controller.reportError(error);
    return false;
  }
}

Future<void> importStartupFiles(
  AppController controller,
  List<String> paths,
) async {
  final result = await controller.execute('import_files', {
    'paths': paths,
    'as_iwads': false,
  });
  final failures = result?['failed'] as List? ?? [];
  if (failures.isNotEmpty) {
    controller.reportError(
      failures
          .map((failure) => '${failure['path']}: ${failure['message']}')
          .join('\n'),
    );
  }
}

/// Native application lifecycle messages share the same guarded user actions.
Future<Object?> handleDesktopLifecycleCall(
  MethodCall call, {
  required AppController controller,
  required Future<bool> Function(String requestId) onQuit,
}) async {
  if (call.method == 'requestQuit') {
    final arguments = call.arguments;
    final requestId = arguments is Map ? arguments['requestId'] : null;
    if (requestId is! String || requestId.isEmpty) return false;
    return await onQuit(requestId);
  }
  if (call.method != 'openFiles') {
    throw MissingPluginException('Unknown lifecycle method: ${call.method}');
  }
  final paths = (call.arguments as List? ?? []).whereType<String>().toList();
  if (paths.isNotEmpty) await importStartupFiles(controller, paths);
  return null;
}
