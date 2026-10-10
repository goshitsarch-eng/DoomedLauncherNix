import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:url_launcher/url_launcher.dart';

/// GTK can acknowledge an open request even when no handler exists. Check the
/// native desktop first, but let Flatpak's portal resolve host applications.
Future<bool> launchExternalUri(Uri uri) async {
  if (defaultTargetPlatform == TargetPlatform.linux &&
      !Platform.environment.containsKey('FLATPAK_ID') &&
      !await canLaunchUrl(uri)) {
    return false;
  }
  return launchUrl(uri, mode: LaunchMode.externalApplication);
}
