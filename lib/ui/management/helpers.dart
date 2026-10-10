import 'package:flutter/material.dart';
import '../../state/app_controller.dart';

int rowId(dynamic value) =>
    value is num ? value.toInt() : int.tryParse('$value') ?? 0;
bool settingFlag(dynamic value, [bool fallback = false]) => value == null
    ? fallback
    : value == true || value == 1 || '$value'.toLowerCase() == 'true';

void showNotice(BuildContext context, String message) {
  if (!context.mounted) return;
  ScaffoldMessenger.maybeOf(
    context,
  )?.showSnackBar(SnackBar(content: Text(message)));
}

Future<Map<String, dynamic>?> perform(
  BuildContext context,
  AppController controller,
  String op, [
  Map<String, dynamic> args = const {},
]) async {
  final result = await controller.execute(op, args);
  if (result == null && context.mounted) {
    showNotice(
      context,
      controller.error ?? 'The operation could not be completed.',
    );
  }
  return result;
}

Future<bool> confirmRemoval(
  BuildContext context,
  String title,
  String message,
) async =>
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
            child: const Text('Remove'),
          ),
        ],
      ),
    ) ??
    false;

String? validateWebUrl(String? value) {
  final uri = Uri.tryParse(value?.trim() ?? '');
  if (uri == null ||
      !['https', 'http'].contains(uri.scheme) ||
      uri.host.isEmpty ||
      uri.userInfo.isNotEmpty) {
    return 'Enter an HTTP or HTTPS URL without credentials.';
  }
  return null;
}

class PageIntro extends StatelessWidget {
  const PageIntro(
    this.title,
    this.description, {
    super.key,
    this.actions = const [],
  });
  final String title;
  final String description;
  final List<Widget> actions;
  @override
  Widget build(BuildContext context) => SizedBox(
    width: double.infinity,
    child: Padding(
      padding: const EdgeInsets.fromLTRB(24, 24, 24, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: Theme.of(context).textTheme.headlineMedium),
          const SizedBox(height: 8),
          Text(description),
          if (actions.isNotEmpty) ...[
            const SizedBox(height: 16),
            Wrap(spacing: 12, runSpacing: 8, children: actions),
          ],
        ],
      ),
    ),
  );
}
