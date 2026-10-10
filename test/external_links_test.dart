import 'package:doomedlauncher/platform/external_links.dart';
import 'package:doomedlauncher/ui/widgets/common.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:url_launcher_platform_interface/url_launcher_platform_interface.dart';

class _DesktopLauncher extends UrlLauncherPlatform {
  bool registered = false;
  final opened = <String>[];
  @override
  get linkDelegate => null;
  @override
  Future<bool> canLaunch(String url) async => registered;
  @override
  Future<bool> launchUrl(String url, LaunchOptions options) async {
    opened.add(url);
    // Reproduces GTK's success acknowledgment even without a handler.
    return true;
  }
}

void main() {
  late UrlLauncherPlatform original;
  late _DesktopLauncher launcher;
  setUp(() {
    original = UrlLauncherPlatform.instance;
    launcher = _DesktopLauncher();
    UrlLauncherPlatform.instance = launcher;
  });
  tearDown(() {
    UrlLauncherPlatform.instance = original;
    debugDefaultTargetPlatformOverride = null;
  });
  testWidgets('missing Linux file handler gives visible feedback', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => TextButton(
              onPressed: () => openLocalFile(context, '/tmp/Épisode 日本語.wad'),
              child: const Text('Open'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    expect(
      find.text('No application is registered to open this file.'),
      findsOneWidget,
    );
    expect(launcher.opened, isEmpty);
  }, variant: TargetPlatformVariant.only(TargetPlatform.linux));
  test('registered Linux handlers receive the encoded URI', () async {
    debugDefaultTargetPlatformOverride = TargetPlatform.linux;
    launcher.registered = true;
    final uri = Uri.file('/tmp/Épisode 日本語.wad');
    expect(await launchExternalUri(uri), isTrue);
    expect(launcher.opened, [uri.toString()]);
  });
  test(
    'missing Linux browser returns failure instead of false success',
    () async {
      debugDefaultTargetPlatformOverride = TargetPlatform.linux;
      expect(
        await launchExternalUri(Uri.parse('https://example.org/')),
        isFalse,
      );
      expect(launcher.opened, isEmpty);
    },
  );
  test('other desktops retain their native launch behavior', () async {
    debugDefaultTargetPlatformOverride = TargetPlatform.windows;
    expect(await launchExternalUri(Uri.parse('https://example.org/')), isTrue);
    expect(launcher.opened, ['https://example.org/']);
  });
}
