import 'dart:io';
import 'package:flutter/material.dart';
import '../../platform/external_links.dart';

int rowId(Map<String, dynamic> row, [String key = 'GameFileID']) =>
    (row[key] as num?)?.toInt() ?? int.tryParse('${row[key]}') ?? -1;
String rowText(Map<String, dynamic> row, String key) => '${row[key] ?? ''}';
String gameTitle(Map<String, dynamic> game) {
  final title = rowText(game, 'Title').trim();
  return title.isEmpty ? rowText(game, 'FileName') : title;
}

void showMessage(BuildContext context, String message) {
  ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
}

Future<void> openLocalFile(BuildContext context, String path) async {
  try {
    if (path.isEmpty || !await launchExternalUri(Uri.file(path))) {
      if (context.mounted) {
        showMessage(context, 'No application is registered to open this file.');
      }
    }
  } catch (error) {
    if (context.mounted) showMessage(context, error.toString());
  }
}

class GameArtwork extends StatelessWidget {
  const GameArtwork({super.key, required this.game, this.fit = BoxFit.contain});
  final Map<String, dynamic> game;
  final BoxFit fit;
  @override
  Widget build(BuildContext context) {
    final path = rowText(game, 'imagePath');
    Widget fallback() => ColoredBox(
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      child: Center(
        child: Icon(
          Icons.sports_esports_outlined,
          size: 44,
          color: Theme.of(context).colorScheme.onSurfaceVariant,
        ),
      ),
    );
    return path.isEmpty
        ? fallback()
        : Image.file(
            File(path),
            fit: fit,
            errorBuilder: (_, _, _) => fallback(),
            gaplessPlayback: true,
          );
  }
}

class InlineError extends StatelessWidget {
  const InlineError(this.message, {super.key, this.onRetry});
  final String message;
  final VoidCallback? onRetry;
  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.symmetric(vertical: 8),
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: Theme.of(context).colorScheme.errorContainer,
      borderRadius: BorderRadius.circular(8),
    ),
    child: Row(
      children: [
        const Icon(Icons.error_outline),
        const SizedBox(width: 10),
        Expanded(child: SelectableText(message)),
        if (onRetry != null)
          TextButton(onPressed: onRetry, child: const Text('Retry')),
      ],
    ),
  );
}

Future<bool> confirmAction(
  BuildContext context, {
  required String title,
  required String message,
  String action = 'Delete',
}) async =>
    await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(title),
        content: Text(message),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: Text(action),
          ),
        ],
      ),
    ) ??
    false;
