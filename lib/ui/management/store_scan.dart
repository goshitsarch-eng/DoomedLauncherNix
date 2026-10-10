import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:path/path.dart' as p;
import '../../state/app_controller.dart';
import 'helpers.dart';

class StoreScanDialog extends StatefulWidget {
  const StoreScanDialog({super.key, required this.controller});
  final AppController controller;
  @override
  State<StoreScanDialog> createState() => _StoreScanDialogState();
}

class _StoreScanDialogState extends State<StoreScanDialog> {
  final Set<String> _roots = {};
  final Map<String, bool> _results = {};
  final Set<String> _selected = {};
  bool _busy = false;
  bool _scanned = false;
  bool _addDoom64 = true;
  String? _doom64;
  String? _error;
  String? _summary;
  @override
  void initState() {
    super.initState();
    _scan();
  }

  Future<void> _scan() async {
    setState(() {
      _busy = true;
      _error = null;
      _summary = null;
    });
    try {
      final result = await widget.controller.backend.call('scan_stores', {
        if (_roots.isNotEmpty) 'roots': _roots.toList(),
      });
      if (!mounted) return;
      setState(() {
        _results.clear();
        for (final path in result['iwads'] as List? ?? []) {
          _results['$path'] = true;
        }
        for (final path in result['pwads'] as List? ?? []) {
          _results.putIfAbsent('$path', () => false);
        }
        _selected
          ..clear()
          ..addAll(_results.keys);
        _doom64 = result['doom64_exe']?.toString();
        if (_doom64?.isEmpty ?? false) _doom64 = null;
        _scanned = true;
      });
    } catch (error) {
      if (mounted) {
        setState(() => _error = 'Could not scan game stores: $error');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _addRoot() async {
    try {
      final root = await getDirectoryPath(
        confirmButtonText: 'Scan this folder',
      );
      if (!mounted || root == null) return;
      setState(() => _roots.add(root));
      await _scan();
    } catch (error) {
      if (mounted) setState(() => _error = 'Could not select a folder: $error');
    }
  }

  Future<void> _import() async {
    setState(() {
      _busy = true;
      _error = null;
      _summary = null;
    });
    var imported = 0;
    final errors = <String>[];
    final succeeded = <String>{};
    for (final asIwads in [true, false]) {
      final paths = _selected
          .where((path) => _results[path] == asIwads)
          .toList();
      if (paths.isEmpty) continue;
      final result = await widget.controller.execute('import_files', {
        'paths': paths,
        'as_iwads': asIwads,
      });
      if (result == null) {
        errors.add(widget.controller.error ?? 'Import failed.');
        continue;
      }
      imported += (result['added'] as List? ?? []).length;
      final failures = result['failed'] as List? ?? [];
      for (final failure in failures) {
        errors.add('${failure['path']}: ${failure['message']}');
      }
      final failedPaths = failures.map((item) => '${item['path']}').toSet();
      succeeded.addAll(paths.where((path) => !failedPaths.contains(path)));
    }
    if (_addDoom64 && _doom64 != null) {
      final exists = widget.controller.ports.any(
        (port) => port['executable'] == _doom64,
      );
      if (!exists) {
        final result = await widget.controller.execute('save_port', {
          'port': {
            'name': 'Doom 64',
            'executable': _doom64,
            'directory': p.dirname(_doom64!),
            'supported_extensions': '.wad',
            'file_option': '-file',
            'extra_parameters': '',
            'alt_save_directory': '',
            'launch_type': 2,
          },
        });
        if (result == null) {
          errors.add(widget.controller.error ?? 'Could not add Doom 64.');
        }
      }
    }
    if (!mounted) return;
    setState(() {
      _busy = false;
      _selected.removeAll(succeeded);
      _summary = 'Imported $imported files.';
      _error = errors.isEmpty ? null : errors.join('\n');
      if (errors.isEmpty) _addDoom64 = false;
    });
    if (errors.isEmpty) {
      showNotice(context, _summary!);
      Navigator.pop(context);
    }
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_busy,
    child: AlertDialog(
      title: const Text('Import from game stores'),
      content: SizedBox(
        width: 680,
        height: 460,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Search Steam and GOG installations, or choose a game folder. Select the game data and add-ons you want in your library.',
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              children: [
                OutlinedButton.icon(
                  onPressed: _busy ? null : _addRoot,
                  icon: const Icon(Icons.folder_open),
                  label: const Text('Choose folder'),
                ),
                TextButton.icon(
                  onPressed: _busy ? null : _scan,
                  icon: const Icon(Icons.refresh),
                  label: const Text('Scan again'),
                ),
              ],
            ),
            if (_roots.isNotEmpty)
              Wrap(
                spacing: 8,
                children: [
                  for (final root in _roots)
                    InputChip(
                      label: Text(p.basename(root)),
                      tooltip: root,
                      onDeleted: _busy
                          ? null
                          : () async {
                              setState(() => _roots.remove(root));
                              await _scan();
                            },
                    ),
                ],
              ),
            if (_busy) const LinearProgressIndicator(),
            if (_summary != null)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: Text(_summary!),
              ),
            if (_error != null)
              Flexible(
                child: SingleChildScrollView(
                  child: SelectableText(
                    _error!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ),
              ),
            if (_results.isNotEmpty)
              CheckboxListTile(
                contentPadding: EdgeInsets.zero,
                title: Text('Select all (${_results.length})'),
                value: _selected.length == _results.length,
                onChanged: _busy
                    ? null
                    : (value) => setState(() {
                        _selected.clear();
                        if (value == true) _selected.addAll(_results.keys);
                      }),
              ),
            Expanded(
              child: _results.isEmpty && _scanned && !_busy
                  ? const Center(
                      child: Text(
                        'No game files found. Choose your installation folder to search it.',
                      ),
                    )
                  : ListView(
                      children: [
                        for (final entry in _results.entries)
                          CheckboxListTile(
                            contentPadding: EdgeInsets.zero,
                            title: Text(p.basename(entry.key)),
                            subtitle: Text(
                              '${entry.value ? 'IWAD · ' : 'Add-on · '}${entry.key}',
                            ),
                            value: _selected.contains(entry.key),
                            onChanged: _busy
                                ? null
                                : (value) => setState(() {
                                    if (value == true) {
                                      _selected.add(entry.key);
                                    } else {
                                      _selected.remove(entry.key);
                                    }
                                  }),
                          ),
                      ],
                    ),
            ),
            if (_doom64 != null)
              CheckboxListTile(
                contentPadding: EdgeInsets.zero,
                title: const Text('Add Doom 64 as a source port'),
                subtitle: Text(_doom64!),
                value: _addDoom64,
                onChanged: _busy
                    ? null
                    : (value) => setState(() => _addDoom64 = value ?? false),
              ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: _busy ? null : () => Navigator.pop(context),
          child: const Text('Close'),
        ),
        FilledButton(
          onPressed:
              _busy || (_selected.isEmpty && (!_addDoom64 || _doom64 == null))
              ? null
              : _import,
          child: Text(_busy ? 'Working…' : 'Import selected'),
        ),
      ],
    ),
  );
}
