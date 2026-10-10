import 'dart:async';
import 'package:flutter/material.dart';
import '../state/app_controller.dart';
import 'widgets/common.dart';

Future<void> showPlayEntry(
  BuildContext context,
  AppController controller,
  Map<String, dynamic> game, {
  String? demoPath,
  bool forceOptions = true,
}) async {
  if (!forceOptions &&
      controller.settings['ShowPlayDialog'] == 'false' &&
      demoPath == null) {
    final defaults = await controller.execute('play_defaults', {
      'id': rowId(game),
    });
    if (defaults != null) {
      final result = await controller.execute('launch', {
        'options': defaults['options'],
      });
      if (result != null && context.mounted) {
        showMessage(context, 'Started ${gameTitle(game)}');
      }
    }
    return;
  }
  if (!context.mounted) return;
  await showDialog<void>(
    context: context,
    builder: (_) =>
        PlayEntryDialog(controller: controller, game: game, demoPath: demoPath),
  );
}

class PlayEntryDialog extends StatefulWidget {
  const PlayEntryDialog({
    super.key,
    required this.controller,
    required this.game,
    this.demoPath,
  });
  final AppController controller;
  final Map<String, dynamic> game;
  final String? demoPath;
  @override
  State<PlayEntryDialog> createState() => _PlayEntryDialogState();
}

class _PlayEntryDialogState extends State<PlayEntryDialog> {
  final _map = TextEditingController();
  final _extra = TextEditingController();
  Map<String, dynamic> _options = {};
  List<String> _maps = [], _additional = [];
  String? _available, _error, _previewError;
  String _preview = '';
  bool _loading = true, _launching = false, _warp = false;
  Timer? _debounce;
  int _previewVersion = 0;
  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _map.dispose();
    _extra.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final result = await widget.controller.backend.call('play_defaults', {
        'id': rowId(widget.game),
      });
      if (!mounted) return;
      _options = Map<String, dynamic>.from(result['options'] as Map? ?? {});
      _options['game_file_id'] = rowId(widget.game);
      _options['remember'] ??= true;
      _options['save_statistics'] ??= true;
      _options['record_demo'] = false;
      if (widget.demoPath != null) _options['play_demo_file'] = widget.demoPath;
      _maps = (result['maps'] as List? ?? []).map((e) => '$e').toList();
      _additional =
          (_options['additional_files'] as List? ??
                  result['additional_files'] as List? ??
                  [])
              .map((e) => '$e')
              .toList();
      _map.text = '${_options['map'] ?? ''}';
      _warp = _map.text.isNotEmpty;
      _extra.text = '${_options['extra_params'] ?? ''}';
      final portIds = widget.controller.ports
          .map((p) => rowId(p, 'id'))
          .toSet();
      if (!portIds.contains(_options['source_port_id'])) {
        _options['source_port_id'] = portIds.firstOrNull;
      }
      final iwadIds = widget.controller.iwads
          .map((p) => rowId(p, 'iwad_id'))
          .toSet();
      if (!iwadIds.contains(_options['iwad_id'])) {
        _options['iwad_id'] = iwadIds.firstOrNull;
      }
      setState(() => _loading = false);
      _schedulePreview();
    } catch (error) {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = '$error';
        });
      }
    }
  }

  int get _launchType =>
      (widget.controller.ports
                  .where(
                    (port) => rowId(port, 'id') == _options['source_port_id'],
                  )
                  .firstOrNull?['launch_type']
              as num?)
          ?.toInt() ??
      0;
  bool get _standalone => _launchType != 0;
  Map<String, dynamic> _collect() => {
    ..._options,
    'map': _warp ? _map.text.trim() : '',
    'skill': _warp ? (_options['skill'] ?? 4) : null,
    'extra_params': _extra.text,
    'additional_files': List<String>.from(_additional),
    if (_standalone) ...{
      'iwad_id': null,
      'map': '',
      'skill': null,
      'extra_params_only': true,
      'load_latest_save': false,
      'save_statistics': false,
      'record_demo': false,
      'play_demo_file': null,
      'additional_files': <String>[],
    },
  };
  void _change(String key, dynamic value) {
    setState(() => _options[key] = value);
    _schedulePreview();
  }

  void _schedulePreview() {
    _debounce?.cancel();
    final version = ++_previewVersion;
    _debounce = Timer(const Duration(milliseconds: 220), () async {
      try {
        final result = await widget.controller.backend.call('launch_preview', {
          'options': _collect(),
        });
        if (mounted && version == _previewVersion) {
          setState(() {
            _preview = '${result['display'] ?? ''}';
            _previewError = null;
          });
        }
      } catch (error) {
        if (mounted && version == _previewVersion) {
          setState(() {
            _previewError = '$error';
            _preview = '';
          });
        }
      }
    });
  }

  Future<void> _launch() async {
    setState(() {
      _launching = true;
      _error = null;
    });
    final result = await widget.controller.execute('launch', {
      'options': _collect(),
    });
    if (!mounted) return;
    if (result != null) {
      Navigator.pop(context);
      showMessage(context, 'Started ${gameTitle(widget.game)}');
    } else {
      setState(() {
        _launching = false;
        _error = widget.controller.error;
      });
    }
  }

  Widget _toggle(String key, String title, {bool defaultValue = false}) =>
      CheckboxListTile(
        contentPadding: EdgeInsets.zero,
        dense: true,
        title: Text(title),
        value: _options[key] as bool? ?? defaultValue,
        onChanged: (value) => _change(key, value ?? false),
      );
  @override
  Widget build(BuildContext context) {
    final mods =
        widget.controller.games
            .where((g) => rowId(g) != rowId(widget.game) && g['isIwad'] != true)
            .map((g) => rowText(g, 'FileName'))
            .where((s) => !_additional.contains(s))
            .toList()
          ..sort();
    if (!mods.contains(_available)) _available = mods.firstOrNull;
    return AlertDialog(
      title: Text(
        widget.demoPath == null
            ? 'Play ${gameTitle(widget.game)}'
            : 'Replay demo',
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
      ),
      content: SizedBox(
        width: 650,
        child: _loading
            ? const SizedBox(
                height: 160,
                child: Center(child: CircularProgressIndicator()),
              )
            : SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (widget.controller.ports.isEmpty)
                      const InlineError('Add a source port before launching.'),
                    DropdownButtonFormField<int>(
                      key: ValueKey('port-${_options['source_port_id']}'),
                      initialValue: _options['source_port_id'] as int?,
                      isExpanded: true,
                      decoration: const InputDecoration(
                        labelText: 'Source port',
                      ),
                      items: widget.controller.ports
                          .map(
                            (p) => DropdownMenuItem(
                              value: rowId(p, 'id'),
                              child: Text(
                                rowText(p, 'name'),
                                overflow: TextOverflow.ellipsis,
                              ),
                            ),
                          )
                          .toList(),
                      onChanged: (value) => _change('source_port_id', value),
                    ),
                    const SizedBox(height: 16),
                    if (_standalone)
                      Padding(
                        padding: const EdgeInsets.symmetric(vertical: 8),
                        child: Text(
                          _launchType == 2
                              ? 'Doom 64 starts as a standalone game. Configure its game options inside the game; extra parameters below are passed directly to its executable.'
                              : 'This utility starts with its configured and extra parameters. Doom game options do not apply.',
                        ),
                      ),
                    if (!_standalone) ...[
                      DropdownButtonFormField<int>(
                        key: ValueKey('iwad-${_options['iwad_id']}'),
                        initialValue: _options['iwad_id'] as int?,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          labelText: 'Base game (IWAD)',
                        ),
                        items: widget.controller.iwads
                            .map(
                              (p) => DropdownMenuItem(
                                value: rowId(p, 'iwad_id'),
                                child: Text(
                                  rowText(p, 'name'),
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                            )
                            .toList(),
                        onChanged: (value) => _change('iwad_id', value),
                      ),
                      const SizedBox(height: 8),
                      CheckboxListTile(
                        contentPadding: EdgeInsets.zero,
                        dense: true,
                        title: const Text('Start at a specific map'),
                        value: _warp,
                        onChanged: (value) {
                          setState(() => _warp = value ?? false);
                          _schedulePreview();
                        },
                      ),
                      Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Expanded(
                            child: TextField(
                              controller: _map,
                              enabled: _warp,
                              onChanged: (_) => _schedulePreview(),
                              decoration: InputDecoration(
                                labelText: 'Map',
                                hintText: 'MAP01 or E1M1',
                                suffixIcon: _maps.isEmpty
                                    ? null
                                    : PopupMenuButton<String>(
                                        tooltip: 'Choose a map',
                                        icon: const Icon(Icons.arrow_drop_down),
                                        enabled: _warp,
                                        onSelected: (value) {
                                          _map.text = value;
                                          _schedulePreview();
                                        },
                                        itemBuilder: (_) => _maps
                                            .map(
                                              (map) => PopupMenuItem(
                                                value: map,
                                                child: Text(map),
                                              ),
                                            )
                                            .toList(),
                                      ),
                              ),
                            ),
                          ),
                          const SizedBox(width: 16),
                          Expanded(
                            child: DropdownButtonFormField<int>(
                              initialValue:
                                  ((_options['skill'] as num?)?.toInt() ?? 4)
                                      .clamp(1, 5),
                              isExpanded: true,
                              decoration: const InputDecoration(
                                labelText: 'Skill',
                              ),
                              items: const [
                                DropdownMenuItem(
                                  value: 1,
                                  child: Text("1 — I'm too young to die"),
                                ),
                                DropdownMenuItem(
                                  value: 2,
                                  child: Text('2 — Hey, not too rough'),
                                ),
                                DropdownMenuItem(
                                  value: 3,
                                  child: Text('3 — Hurt me plenty'),
                                ),
                                DropdownMenuItem(
                                  value: 4,
                                  child: Text('4 — Ultra-Violence'),
                                ),
                                DropdownMenuItem(
                                  value: 5,
                                  child: Text('5 — Nightmare!'),
                                ),
                              ],
                              onChanged: _warp
                                  ? (value) => _change('skill', value)
                                  : null,
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 16),
                    ],
                    TextField(
                      controller: _extra,
                      onChanged: (_) => _schedulePreview(),
                      decoration: const InputDecoration(
                        labelText: 'Extra parameters',
                        hintText: '-nosound +vid_fullscreen 0',
                      ),
                    ),
                    if (!_standalone) ...[
                      _toggle('extra_params_only', 'Use extra parameters only'),
                      _toggle('load_latest_save', 'Load latest save'),
                      _toggle(
                        'save_statistics',
                        'Save play statistics',
                        defaultValue: true,
                      ),
                      if (widget.demoPath == null)
                        _toggle('record_demo', 'Record demo'),
                    ],
                    _toggle(
                      'remember',
                      'Remember these settings',
                      defaultValue: true,
                    ),
                    if (!_standalone && widget.demoPath != null)
                      Padding(
                        padding: const EdgeInsets.all(8),
                        child: SelectableText('Demo: ${widget.demoPath}'),
                      ),
                    if (!_standalone) ...[
                      const Divider(),
                      const Text('Additional files · loaded in this order'),
                      const SizedBox(height: 8),
                      Row(
                        children: [
                          Expanded(
                            child: DropdownButtonFormField<String>(
                              key: ValueKey(
                                'additional-$_available-${mods.length}',
                              ),
                              initialValue: _available,
                              isExpanded: true,
                              decoration: const InputDecoration(
                                labelText: 'Library file',
                              ),
                              items: mods
                                  .map(
                                    (name) => DropdownMenuItem(
                                      value: name,
                                      child: Text(
                                        name,
                                        overflow: TextOverflow.ellipsis,
                                      ),
                                    ),
                                  )
                                  .toList(),
                              onChanged: (value) =>
                                  setState(() => _available = value),
                            ),
                          ),
                          const SizedBox(width: 8),
                          IconButton.filledTonal(
                            tooltip: 'Add file',
                            icon: const Icon(Icons.add),
                            onPressed: _available == null
                                ? null
                                : () {
                                    setState(
                                      () => _additional.add(_available!),
                                    );
                                    _schedulePreview();
                                  },
                          ),
                        ],
                      ),
                      for (var i = 0; i < _additional.length; i++)
                        Row(
                          children: [
                            Expanded(
                              child: Text(
                                '${i + 1}. ${_additional[i]}',
                                overflow: TextOverflow.ellipsis,
                              ),
                            ),
                            IconButton(
                              tooltip: 'Move up',
                              icon: const Icon(Icons.arrow_upward),
                              onPressed: i == 0
                                  ? null
                                  : () {
                                      setState(() {
                                        final previous = _additional[i - 1];
                                        _additional[i - 1] = _additional[i];
                                        _additional[i] = previous;
                                      });
                                      _schedulePreview();
                                    },
                            ),
                            IconButton(
                              tooltip: 'Move down',
                              icon: const Icon(Icons.arrow_downward),
                              onPressed: i == _additional.length - 1
                                  ? null
                                  : () {
                                      setState(() {
                                        final next = _additional[i + 1];
                                        _additional[i + 1] = _additional[i];
                                        _additional[i] = next;
                                      });
                                      _schedulePreview();
                                    },
                            ),
                            IconButton(
                              tooltip: 'Remove file',
                              icon: const Icon(Icons.close),
                              onPressed: () {
                                setState(() => _additional.removeAt(i));
                                _schedulePreview();
                              },
                            ),
                          ],
                        ),
                      const SizedBox(height: 16),
                    ],
                    Text(
                      'Command preview',
                      style: Theme.of(context).textTheme.labelLarge,
                    ),
                    const SizedBox(height: 8),
                    if (_previewError != null)
                      InlineError(_previewError!)
                    else
                      SelectableText(
                        _preview.isEmpty ? 'Preparing command…' : _preview,
                        style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          fontFamily: 'monospace',
                        ),
                      ),
                    if (_error != null) InlineError(_error!),
                  ],
                ),
              ),
      ),
      actions: [
        TextButton(
          onPressed: _launching ? null : () => Navigator.pop(context),
          child: const Text('Cancel'),
        ),
        FilledButton.icon(
          onPressed: _loading || _launching || widget.controller.ports.isEmpty
              ? null
              : _launch,
          icon: const Icon(Icons.play_arrow),
          label: Text(_launching ? 'Starting…' : 'Launch'),
        ),
      ],
    );
  }
}
