import 'package:flutter/material.dart';
import '../state/app_controller.dart';
import 'widgets/common.dart';

Future<void> showEditEntry(
  BuildContext context,
  AppController controller,
  Map<String, dynamic> game,
) => showDialog<void>(
  context: context,
  builder: (_) => EditEntryDialog(controller: controller, game: game),
);

class EditEntryDialog extends StatefulWidget {
  const EditEntryDialog({
    super.key,
    required this.controller,
    required this.game,
  });
  final AppController controller;
  final Map<String, dynamic> game;
  @override
  State<EditEntryDialog> createState() => _EditEntryDialogState();
}

class _EditEntryDialogState extends State<EditEntryDialog> {
  final _fields = <String, TextEditingController>{};
  late Set<int> _tags;
  late int _rating;
  bool _saving = false;
  String? _error;
  @override
  void initState() {
    super.initState();
    for (final key in [
      'Title',
      'Author',
      'Description',
      'Comments',
      'ReleaseDate',
    ]) {
      _fields[key] = TextEditingController(text: rowText(widget.game, key));
    }
    _rating = ((widget.game['Rating'] as num?)?.toInt() ?? 0).clamp(0, 5);
    _tags = (widget.game['tagIds'] as List? ?? [])
        .whereType<num>()
        .map((id) => id.toInt())
        .toSet();
  }

  @override
  void dispose() {
    for (final field in _fields.values) {
      field.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    setState(() {
      _saving = true;
      _error = null;
    });
    final result = await widget.controller.execute('edit_entry', {
      'id': rowId(widget.game),
      'fields': {
        for (final entry in _fields.entries) entry.key: entry.value.text,
        'Rating': _rating,
      },
      'tag_ids': _tags.toList(),
    });
    if (!mounted) return;
    if (result != null) {
      Navigator.pop(context);
    } else {
      setState(() {
        _saving = false;
        _error = widget.controller.error;
      });
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Edit library entry'),
    content: SizedBox(
      width: 620,
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            for (final entry in _fields.entries)
              Padding(
                padding: const EdgeInsets.only(bottom: 14),
                child: TextField(
                  controller: entry.value,
                  maxLines: ['Description', 'Comments'].contains(entry.key)
                      ? 3
                      : 1,
                  decoration: InputDecoration(
                    labelText: entry.key == 'ReleaseDate'
                        ? 'Release date'
                        : entry.key,
                    hintText: entry.key == 'ReleaseDate' ? 'YYYY-MM-DD' : null,
                  ),
                ),
              ),
            DropdownButtonFormField<int>(
              initialValue: _rating,
              decoration: const InputDecoration(labelText: 'Your rating'),
              items: List.generate(
                6,
                (n) => DropdownMenuItem(
                  value: n,
                  child: Text(n == 0 ? 'Unrated' : '$n / 5'),
                ),
              ),
              onChanged: (value) => setState(() => _rating = value ?? 0),
            ),
            const SizedBox(height: 18),
            const Text('Tags'),
            const SizedBox(height: 8),
            if (widget.controller.tags.isEmpty)
              const Text(
                'Create tags in the Tags page to organize this entry.',
              ),
            Wrap(
              spacing: 8,
              runSpacing: 4,
              children: widget.controller.tags.map((tag) {
                final id = rowId(tag, 'tag_id');
                return FilterChip(
                  label: Text(rowText(tag, 'name')),
                  selected: _tags.contains(id),
                  onSelected: (value) => setState(() {
                    value ? _tags.add(id) : _tags.remove(id);
                  }),
                );
              }).toList(),
            ),
            if (_error != null) InlineError(_error!),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: _saving ? null : () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: _saving ? null : _save,
        child: Text(_saving ? 'Saving…' : 'Save'),
      ),
    ],
  );
}
