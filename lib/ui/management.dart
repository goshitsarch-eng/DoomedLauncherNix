import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:path/path.dart' as p;
import '../state/app_controller.dart';
import 'management/helpers.dart';

export 'management/settings.dart';
export 'management/store_scan.dart';

class SourcePortsPage extends StatelessWidget {
  const SourcePortsPage({super.key, required this.controller});
  final AppController controller;

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: controller,
    builder: (context, _) => Column(
      children: [
        PageIntro(
          'Source ports',
          'Choose the engines and utilities used to open your Doom files.',
          actions: [
            FilledButton.icon(
              onPressed: controller.busy
                  ? null
                  : () => showDialog<void>(
                      context: context,
                      builder: (_) =>
                          SourcePortEditDialog(controller: controller),
                    ),
              icon: const Icon(Icons.add),
              label: const Text('Add source port'),
            ),
            OutlinedButton.icon(
              onPressed: controller.busy
                  ? null
                  : () async {
                      final result = await perform(
                        context,
                        controller,
                        'detect_ports',
                      );
                      if (result != null && context.mounted) {
                        final count = rowId(result['added']);
                        showNotice(
                          context,
                          count == 0
                              ? 'No new ports found. You can add an executable manually.'
                              : 'Added $count source ports.',
                        );
                      }
                    },
              icon: const Icon(Icons.search),
              label: const Text('Detect installed ports'),
            ),
          ],
        ),
        if (controller.busy) const LinearProgressIndicator(),
        Expanded(
          child: controller.ports.isEmpty
              ? const Center(
                  child: Padding(
                    padding: EdgeInsets.all(24),
                    child: Text(
                      'Add a source port, such as GZDoom, to start playing.',
                    ),
                  ),
                )
              : ListView.builder(
                  itemCount: controller.ports.length,
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  itemBuilder: (context, index) {
                    final port = controller.ports[index];
                    final type = rowId(port['launch_type']);
                    return Card(
                      child: ListTile(
                        leading: Icon(
                          type == 1
                              ? Icons.build_outlined
                              : Icons.sports_esports_outlined,
                        ),
                        title: Text('${port['name']}'),
                        subtitle: Text(
                          '${port['executable']}\n${type == 2
                              ? 'Doom 64'
                              : type == 1
                              ? 'Utility'
                              : 'Doom source port'}',
                        ),
                        isThreeLine: true,
                        onTap: () => showDialog<void>(
                          context: context,
                          builder: (_) => SourcePortEditDialog(
                            controller: controller,
                            port: port,
                          ),
                        ),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            IconButton(
                              tooltip: 'Edit ${port['name']}',
                              icon: const Icon(Icons.edit_outlined),
                              onPressed: () => showDialog<void>(
                                context: context,
                                builder: (_) => SourcePortEditDialog(
                                  controller: controller,
                                  port: port,
                                ),
                              ),
                            ),
                            IconButton(
                              tooltip: 'Remove ${port['name']}',
                              icon: const Icon(Icons.delete_outline),
                              onPressed: controller.busy
                                  ? null
                                  : () async {
                                      final confirmed = await confirmRemoval(
                                        context,
                                        'Remove source port?',
                                        'Remove ${port['name']} from the launcher? The executable will remain on disk.',
                                      );
                                      if (confirmed && context.mounted) {
                                        await perform(
                                          context,
                                          controller,
                                          'delete_port',
                                          {'id': port['id']},
                                        );
                                      }
                                    },
                            ),
                          ],
                        ),
                      ),
                    );
                  },
                ),
        ),
      ],
    ),
  );
}

class SourcePortEditDialog extends StatefulWidget {
  const SourcePortEditDialog({super.key, required this.controller, this.port});
  final AppController controller;
  final Map<String, dynamic>? port;
  @override
  State<SourcePortEditDialog> createState() => _SourcePortEditDialogState();
}

class _SourcePortEditDialogState extends State<SourcePortEditDialog> {
  final _form = GlobalKey<FormState>();
  late final Map<String, TextEditingController> _fields;
  late int _launchType;
  bool _saving = false;
  String? _error;
  @override
  void initState() {
    super.initState();
    const defaults = {
      'name': '',
      'executable': '',
      'directory': '',
      'supported_extensions': '.wad,.deh,.bex,.pk3,.pk7,.ipk3,.zip',
      'file_option': '-file',
      'extra_parameters': '',
      'alt_save_directory': '',
    };
    _fields = defaults.map(
      (key, value) => MapEntry(
        key,
        TextEditingController(text: '${widget.port?[key] ?? value}'),
      ),
    );
    _launchType = rowId(widget.port?['launch_type']);
  }

  @override
  void dispose() {
    for (final field in _fields.values) {
      field.dispose();
    }
    super.dispose();
  }

  Future<void> _pick(String field, {bool directory = false}) async {
    try {
      final path = directory
          ? await getDirectoryPath(confirmButtonText: 'Select folder')
          : (await openFile(confirmButtonText: 'Select executable'))?.path;
      if (!mounted || path == null) return;
      _fields[field]!.text = path;
      if (field == 'executable' && _fields['directory']!.text.isEmpty) {
        _fields['directory']!.text = p.dirname(path);
      }
    } catch (error) {
      if (mounted) {
        setState(() => _error = 'Could not open the file chooser: $error');
      }
    }
  }

  Future<void> _save() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    final result = await widget.controller.execute('save_port', {
      'port': {
        if (widget.port != null) 'id': widget.port!['id'],
        ..._fields.map((key, field) => MapEntry(key, field.text.trim())),
        'launch_type': _launchType,
      },
    });
    if (!mounted) return;
    if (result != null) {
      Navigator.pop(context);
    } else {
      setState(() {
        _saving = false;
        _error = widget.controller.error ?? 'Could not save source port.';
      });
    }
  }

  Widget _field(
    String key,
    String label, {
    String? hint,
    bool required = false,
    bool picker = false,
    bool directory = false,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 16),
    child: TextFormField(
      controller: _fields[key],
      enabled: !_saving,
      validator: required
          ? (value) => value == null || value.trim().isEmpty
                ? '$label is required.'
                : null
          : null,
      decoration: InputDecoration(
        labelText: label,
        helperText: hint,
        suffixIcon: picker
            ? IconButton(
                tooltip: 'Browse $label',
                onPressed: _saving
                    ? null
                    : () => _pick(key, directory: directory),
                icon: const Icon(Icons.folder_open),
              )
            : null,
      ),
    ),
  );
  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_saving,
    child: AlertDialog(
      title: Text(widget.port == null ? 'Add source port' : 'Edit source port'),
      content: SizedBox(
        width: 580,
        child: SingleChildScrollView(
          child: Form(
            key: _form,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                _field('name', 'Name', required: true),
                _field(
                  'executable',
                  'Executable',
                  required: true,
                  picker: true,
                  hint: 'A command, a full path, or flatpak:app.id',
                ),
                DropdownButtonFormField<int>(
                  initialValue: _launchType,
                  decoration: const InputDecoration(labelText: 'Launch type'),
                  items: [
                    const DropdownMenuItem(
                      value: 0,
                      child: Text('Doom source port'),
                    ),
                    const DropdownMenuItem(value: 1, child: Text('Utility')),
                    const DropdownMenuItem(value: 2, child: Text('Doom 64')),
                    if (![0, 1, 2].contains(_launchType))
                      DropdownMenuItem(
                        value: _launchType,
                        child: Text('Existing type $_launchType'),
                      ),
                  ],
                  onChanged: _saving
                      ? null
                      : (value) => setState(() => _launchType = value!),
                ),
                const SizedBox(height: 16),
                _field(
                  'directory',
                  'Working directory',
                  picker: true,
                  directory: true,
                ),
                _field(
                  'supported_extensions',
                  'Supported extensions',
                  hint: 'Comma-separated extensions, for example .wad,.pk3',
                ),
                _field(
                  'file_option',
                  'File parameter',
                  hint:
                      'Usually -file; leave empty for utilities that take a file directly',
                ),
                _field('extra_parameters', 'Extra parameters'),
                _field(
                  'alt_save_directory',
                  'Save game directory',
                  picker: true,
                  directory: true,
                ),
                if (_error != null)
                  SelectableText(
                    _error!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
              ],
            ),
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
    ),
  );
}

class TagsPage extends StatelessWidget {
  const TagsPage({super.key, required this.controller});
  final AppController controller;
  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: controller,
    builder: (context, _) => Column(
      children: [
        PageIntro(
          'Tags',
          'Group your library and show favorite tags as library tabs.',
          actions: [
            FilledButton.icon(
              onPressed: controller.busy
                  ? null
                  : () => showDialog<void>(
                      context: context,
                      builder: (_) => _TagDialog(controller: controller),
                    ),
              icon: const Icon(Icons.add),
              label: const Text('New tag'),
            ),
          ],
        ),
        if (controller.busy) const LinearProgressIndicator(),
        Expanded(
          child: controller.tags.isEmpty
              ? const Center(
                  child: Text(
                    'Create a tag, then assign it from an entry’s edit dialog.',
                  ),
                )
              : ListView.builder(
                  itemCount: controller.tags.length,
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  itemBuilder: (context, index) {
                    final tag = controller.tags[index];
                    return Card(
                      child: ListTile(
                        leading: const Icon(Icons.label_outline),
                        title: Text('${tag['name']}'),
                        subtitle: Text(
                          settingFlag(tag['has_tab'])
                              ? 'Shown as a library tab'
                              : 'Library tag',
                        ),
                        onTap: () => showDialog<void>(
                          context: context,
                          builder: (_) =>
                              _TagDialog(controller: controller, tag: tag),
                        ),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Tooltip(
                              message: 'Show as library tab',
                              child: Switch(
                                value: settingFlag(tag['has_tab']),
                                onChanged: controller.busy
                                    ? null
                                    : (value) => perform(
                                        context,
                                        controller,
                                        'save_tag',
                                        {
                                          'id': tag['tag_id'],
                                          'name': tag['name'],
                                          'has_tab': value,
                                        },
                                      ),
                              ),
                            ),
                            IconButton(
                              tooltip: 'Rename ${tag['name']}',
                              icon: const Icon(Icons.edit_outlined),
                              onPressed: () => showDialog<void>(
                                context: context,
                                builder: (_) => _TagDialog(
                                  controller: controller,
                                  tag: tag,
                                ),
                              ),
                            ),
                            IconButton(
                              tooltip: 'Delete ${tag['name']}',
                              icon: const Icon(Icons.delete_outline),
                              onPressed: controller.busy
                                  ? null
                                  : () async {
                                      final confirmed = await confirmRemoval(
                                        context,
                                        'Remove tag?',
                                        'Remove ${tag['name']} from all files? Your files and other tags will remain.',
                                      );
                                      if (confirmed && context.mounted) {
                                        await perform(
                                          context,
                                          controller,
                                          'delete_tag',
                                          {'id': tag['tag_id']},
                                        );
                                      }
                                    },
                            ),
                          ],
                        ),
                      ),
                    );
                  },
                ),
        ),
      ],
    ),
  );
}

class _TagDialog extends StatefulWidget {
  const _TagDialog({required this.controller, this.tag});
  final AppController controller;
  final Map<String, dynamic>? tag;
  @override
  State<_TagDialog> createState() => _TagDialogState();
}

class _TagDialogState extends State<_TagDialog> {
  final _form = GlobalKey<FormState>();
  late final TextEditingController _name = TextEditingController(
    text: widget.tag?['name']?.toString() ?? '',
  );
  late bool _showTab = settingFlag(widget.tag?['has_tab']);
  bool _saving = false;
  String? _error;
  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    final result = await widget.controller.execute('save_tag', {
      'name': _name.text.trim(),
      'has_tab': _showTab,
      if (widget.tag != null) 'id': widget.tag!['tag_id'],
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
  Widget build(BuildContext context) => PopScope(
    canPop: !_saving,
    child: AlertDialog(
      title: Text(widget.tag == null ? 'New tag' : 'Edit tag'),
      content: SizedBox(
        width: 380,
        child: Form(
          key: _form,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextFormField(
                controller: _name,
                autofocus: true,
                enabled: !_saving,
                decoration: const InputDecoration(labelText: 'Tag name'),
                validator: (value) => value == null || value.trim().isEmpty
                    ? 'Enter a tag name.'
                    : null,
                onFieldSubmitted: (_) {
                  if (!_saving) _save();
                },
              ),
              SwitchListTile(
                contentPadding: EdgeInsets.zero,
                title: const Text('Show as library tab'),
                value: _showTab,
                onChanged: _saving
                    ? null
                    : (value) => setState(() => _showTab = value),
              ),
              if (_error != null)
                Text(
                  _error!,
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
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
    ),
  );
}
