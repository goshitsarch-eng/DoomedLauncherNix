import 'package:flutter/material.dart';
import '../../state/app_controller.dart';
import 'helpers.dart';

class SettingsPage extends StatefulWidget {
  const SettingsPage({super.key, required this.controller});
  final AppController controller;
  @override
  State<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends State<SettingsPage> {
  final _networkForm = GlobalKey<FormState>();
  late final Map<String, TextEditingController> _fields;
  static const _defaults = {
    'MirrorUrl': 'https://www.quaddicted.com/files/idgames/',
    'IdGamesUrl': 'https://www.doomworld.com/idgames/',
    'ApiPage': 'api/api.php',
  };
  bool _networkDirty = false;
  @override
  void initState() {
    super.initState();
    _fields = _defaults.map(
      (key, fallback) => MapEntry(
        key,
        TextEditingController(
          text: '${widget.controller.settings[key] ?? fallback}',
        ),
      ),
    );
    widget.controller.addListener(_sync);
  }

  void _sync() {
    if (!mounted) return;
    for (final key in _fields.keys) {
      if (!_networkDirty) {
        final value = '${widget.controller.settings[key] ?? _defaults[key]}';
        if (_fields[key]!.text != value) _fields[key]!.text = value;
      }
    }
    setState(() {});
  }

  @override
  void dispose() {
    widget.controller.removeListener(_sync);
    for (final field in _fields.values) {
      field.dispose();
    }
    super.dispose();
  }

  Future<void> _save(Map<String, String> values) async {
    await perform(context, widget.controller, 'update_settings', {
      'values': values,
    });
  }

  Future<void> _saveNetwork() async {
    if (!_networkForm.currentState!.validate()) {
      return;
    }
    const keys = ['MirrorUrl', 'IdGamesUrl', 'ApiPage'];
    final result = await perform(
      context,
      widget.controller,
      'update_settings',
      {
        'values': {for (final key in keys) key: _fields[key]!.text.trim()},
      },
    );
    if (result != null && mounted) {
      setState(() => _networkDirty = false);
      showNotice(context, 'Settings saved.');
    }
  }

  Widget _section(BuildContext context, String title, List<Widget> children) =>
      Card(
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 16),
              ...children,
            ],
          ),
        ),
      );
  Widget _select(
    String label,
    String key,
    Map<String, String> choices,
    String fallback,
  ) {
    final raw = '${widget.controller.settings[key] ?? fallback}';
    final value = choices.containsKey(raw) ? raw : fallback;
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: DropdownButtonFormField<String>(
        key: ValueKey('$key:$value'),
        initialValue: value,
        isExpanded: true,
        decoration: InputDecoration(labelText: label),
        items: choices.entries
            .map(
              (entry) =>
                  DropdownMenuItem(value: entry.key, child: Text(entry.value)),
            )
            .toList(),
        onChanged: widget.controller.busy
            ? null
            : (value) {
                if (value != null) _save({key: value});
              },
      ),
    );
  }

  Widget _text(
    String key,
    String label, {
    String? helper,
    String? Function(String?)? validator,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 16),
    child: TextFormField(
      controller: _fields[key],
      enabled: !widget.controller.busy,
      decoration: InputDecoration(
        labelText: label,
        helperText: helper,
        helperMaxLines: 3,
      ),
      validator: validator,
      onChanged: (_) => setState(() => _networkDirty = true),
    ),
  );
  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final settings = controller.settings;
    final portOptions = <String, String>{
      '0': 'Choose automatically',
      for (final port in controller.ports.where(
        (port) => rowId(port['launch_type']) != 1,
      ))
        '${port['id']}': '${port['name']}',
    };
    final iwadOptions = <String, String>{
      '0': 'Choose automatically',
      for (final iwad in controller.iwads)
        '${iwad['iwad_id']}': '${iwad['name']}',
    };
    final views =
        '${settings['VisibleViews'] ?? 'Recent;Untagged;IWads;Id Games'}'
            .split(';')
            .where((v) => v.isNotEmpty)
            .toSet();
    return ListView(
      padding: const EdgeInsets.only(bottom: 24),
      children: [
        const PageIntro(
          'Settings',
          'Customize the library, launch defaults, and download sources.',
        ),
        if (controller.busy) const LinearProgressIndicator(),
        Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 850),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: Column(
                children: [
                  _section(context, 'Appearance and library', [
                    _select('Color scheme', 'ColorThemeType', {
                      'System': 'Follow system',
                      'Default': 'Light',
                      'Dark': 'Dark',
                    }, 'System'),
                    _select('Library layout', 'GameFileViewType', {
                      'GridView': 'List',
                      'TileView': 'Tiles',
                    }, 'GridView'),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text(
                        'Show the play dialog before launching',
                      ),
                      value: settingFlag(settings['ShowPlayDialog'], true),
                      onChanged: controller.busy
                          ? null
                          : (value) => _save({'ShowPlayDialog': '$value'}),
                    ),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text(
                        'Extract title pictures when importing',
                      ),
                      value: settingFlag(
                        settings['AutomaticallyPullTitlpic'],
                        true,
                      ),
                      onChanged: controller.busy
                          ? null
                          : (value) =>
                                _save({'AutomaticallyPullTitlpic': '$value'}),
                    ),
                    const Divider(),
                    const Text('Library tabs'),
                    Wrap(
                      spacing: 12,
                      children: [
                        for (final entry in {
                          'Recent': 'Recent',
                          'Untagged': 'Untagged',
                          'IWads': 'IWADs',
                          'Id Games': 'idgames',
                        }.entries)
                          FilterChip(
                            label: Text(entry.value),
                            selected: views.contains(entry.key),
                            onSelected: controller.busy
                                ? null
                                : (selected) {
                                    final next = {...views};
                                    if (selected) {
                                      next.add(entry.key);
                                    } else {
                                      next.remove(entry.key);
                                    }
                                    _save({'VisibleViews': next.join(';')});
                                  },
                          ),
                      ],
                    ),
                  ]),
                  _section(context, 'Launch defaults', [
                    _select(
                      'Default source port',
                      'DefaultSourcePort',
                      portOptions,
                      '0',
                    ),
                    _select('Default IWAD', 'DefaultIWad', iwadOptions, '0'),
                    const Text(
                      'Per-game launch choices take precedence when you remember them in the play dialog.',
                    ),
                  ]),
                  _section(context, 'Downloads and idgames', [
                    Form(
                      key: _networkForm,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          _text(
                            'MirrorUrl',
                            'Download mirror URL',
                            validator: validateWebUrl,
                            helper:
                                'The root of an idgames mirror, including the trailing slash.',
                          ),
                          _text(
                            'IdGamesUrl',
                            'idgames website URL',
                            validator: validateWebUrl,
                          ),
                          _text(
                            'ApiPage',
                            'API path',
                            helper: 'Relative to the idgames website URL.',
                            validator: (value) {
                              final raw = value?.trim() ?? '';
                              final uri = Uri.tryParse(raw);
                              if (raw.isEmpty ||
                                  uri == null ||
                                  uri.hasScheme ||
                                  uri.hasAuthority ||
                                  raw.contains('..') ||
                                  raw.startsWith('/') ||
                                  raw.contains('\\')) {
                                return 'Enter a relative API path, such as api/api.php.';
                              }
                              return null;
                            },
                          ),
                          FilledButton(
                            onPressed: controller.busy || !_networkDirty
                                ? null
                                : _saveNetwork,
                            child: const Text('Save download settings'),
                          ),
                        ],
                      ),
                    ),
                  ]),
                  Card(
                    child: ExpansionTile(
                      title: const Text('Advanced'),
                      childrenPadding: const EdgeInsets.all(20),
                      children: [
                        const Text(
                          'Window size and the last selected library tab are saved automatically.',
                        ),
                        const SizedBox(height: 16),
                        SelectableText(
                          controller.snapshot['data_root'] == null
                              ? 'The library is not open.'
                              : 'Library data: ${controller.snapshot['data_root']}',
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'Version: ${controller.snapshot['version'] ?? '5.0.0-alpha.1'}',
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}
