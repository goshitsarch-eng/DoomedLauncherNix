import 'dart:io';
import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import '../state/app_controller.dart';
import 'edit_dialog.dart';
import 'play_dialog.dart';
import 'widgets/common.dart';

Future<void> showEntryDetails(
  BuildContext context,
  AppController controller,
  Map<String, dynamic> game,
) => showDialog<void>(
  context: context,
  builder: (context) => Dialog(
    child: ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: 820, maxHeight: 850),
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 12, 8, 4),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    'Entry details',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ),
                IconButton(
                  tooltip: 'Close',
                  onPressed: () => Navigator.pop(context),
                  icon: const Icon(Icons.close),
                ),
              ],
            ),
          ),
          Expanded(
            child: EntryDetailsPanel(controller: controller, game: game),
          ),
        ],
      ),
    ),
  ),
);

class EntryDetailsPanel extends StatefulWidget {
  const EntryDetailsPanel({
    super.key,
    required this.controller,
    required this.game,
  });
  final AppController controller;
  final Map<String, dynamic> game;
  @override
  State<EntryDetailsPanel> createState() => _EntryDetailsPanelState();
}

class _EntryDetailsPanelState extends State<EntryDetailsPanel> {
  Map<String, dynamic>? _details;
  String? _error;
  bool _loading = true;
  int _generation = 0;
  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didUpdateWidget(EntryDetailsPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.game != widget.game) _load();
  }

  Future<void> _load() async {
    final generation = ++_generation;
    try {
      final details = await widget.controller.backend.call('entry_details', {
        'id': rowId(widget.game),
      });
      if (mounted && generation == _generation) {
        setState(() {
          _details = details;
          _error = null;
          _loading = false;
        });
      }
    } catch (error) {
      if (mounted && generation == _generation) {
        setState(() {
          _error = '$error';
          _loading = false;
        });
      }
    }
  }

  Future<void> _import() async {
    try {
      final files = await openFiles(confirmButtonText: 'Import');
      if (files.isEmpty) return;
      final result = await widget.controller.execute('import_associations', {
        'id': rowId(widget.game),
        'paths': files.map((f) => f.path).toList(),
      });
      if (mounted && result != null) {
        final failures = result['failed'] as List? ?? [];
        if (failures.isNotEmpty) {
          showMessage(
            context,
            failures.map((e) => '${e['path']}: ${e['message']}').join('\n'),
          );
        }
        await _load();
      }
    } catch (error) {
      if (mounted) showMessage(context, '$error');
    }
  }

  Future<void> _remove(Map<String, dynamic> file) async {
    if (!await confirmAction(
      context,
      title: 'Remove associated file?',
      message: 'Remove ${rowText(file, 'FileName')} and its managed copy?',
      action: 'Remove',
    )) {
      return;
    }
    await widget.controller.execute('delete_association', {
      'file_id': rowId(file, 'FileID'),
    });
    await _load();
  }

  Future<void> _openText([String? member]) async {
    final result = await widget.controller.execute('open_text', {
      'id': rowId(widget.game),
      'member': ?member,
    });
    if (mounted && result != null) {
      await openLocalFile(context, '${result['path']}');
    }
  }

  @override
  Widget build(BuildContext context) {
    final game = _details?['game'] as Map<String, dynamic>? ?? widget.game;
    final files = (_details?['files'] as List? ?? [])
        .map((e) => Map<String, dynamic>.from(e as Map))
        .toList();
    final stats = (_details?['stats'] as List? ?? [])
        .map((e) => Map<String, dynamic>.from(e as Map))
        .toList();
    final texts = (_details?['text_files'] as List? ?? [])
        .map((e) => '$e')
        .toList();
    return SelectionArea(
      child: ListView(
        padding: const EdgeInsets.all(20),
        children: [
          ClipRRect(
            borderRadius: BorderRadius.circular(12),
            child: SizedBox(height: 190, child: GameArtwork(game: game)),
          ),
          const SizedBox(height: 18),
          Text(
            gameTitle(game),
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          if (rowText(game, 'Author').isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 6),
              child: Text('By ${rowText(game, 'Author')}'),
            ),
          const SizedBox(height: 8),
          Text(
            rowText(game, 'FileName'),
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              FilledButton.icon(
                onPressed: game['pathError'] != null
                    ? null
                    : () => showPlayEntry(context, widget.controller, game),
                icon: const Icon(Icons.play_arrow),
                label: const Text('Play'),
              ),
              OutlinedButton.icon(
                onPressed: () async {
                  await showEditEntry(context, widget.controller, game);
                  await _load();
                },
                icon: const Icon(Icons.edit_outlined),
                label: const Text('Edit'),
              ),
              IconButton(
                tooltip: 'Open file',
                onPressed:
                    game['pathError'] != null || rowText(game, 'path').isEmpty
                    ? null
                    : () => openLocalFile(context, rowText(game, 'path')),
                icon: const Icon(Icons.open_in_new),
              ),
            ],
          ),
          const SizedBox(height: 16),
          if ((game['Rating'] as num? ?? 0) > 0)
            Row(
              children: List.generate(
                5,
                (i) => Icon(
                  i < (game['Rating'] as num) ? Icons.star : Icons.star_outline,
                  color: Theme.of(context).colorScheme.primary,
                  size: 20,
                ),
              ),
            ),
          Wrap(
            spacing: 6,
            children: widget.controller.tags
                .where(
                  (tag) => (game['tagIds'] as List? ?? []).contains(
                    rowId(tag, 'tag_id'),
                  ),
                )
                .map(
                  (tag) => Chip(
                    label: Text(rowText(tag, 'name')),
                    visualDensity: VisualDensity.compact,
                  ),
                )
                .toList(),
          ),
          for (final pair in <(String, String)>[
            ('Maps', rowText(game, 'Map')),
            ('Release date', rowText(game, 'ReleaseDate')),
            ('Imported', rowText(game, 'Downloaded')),
            ('Last played', rowText(game, 'LastPlayed')),
            ('Time played', '${game['MinutesPlayed'] ?? 0} minutes'),
          ])
            if (pair.$2.isNotEmpty)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 3),
                child: Text(
                  '${pair.$1}: ${pair.$2}',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ),
          if (rowText(game, 'Description').isNotEmpty) ...[
            const SizedBox(height: 20),
            Text('Description', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            Text(rowText(game, 'Description')),
          ],
          if (rowText(game, 'Comments').isNotEmpty) ...[
            const SizedBox(height: 20),
            Text('Your notes', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            Text(rowText(game, 'Comments')),
          ],
          if (game['pathError'] != null) InlineError('${game['pathError']}'),
          if (_details?['text_error'] != null)
            InlineError('${_details!['text_error']}'),
          if (_loading)
            const Padding(
              padding: EdgeInsets.all(12),
              child: LinearProgressIndicator(),
            ),
          if (_error != null) InlineError(_error!, onRetry: _load),
          if (texts.isNotEmpty) ...[
            const SizedBox(height: 20),
            Text('Text files', style: Theme.of(context).textTheme.titleMedium),
            for (final text in texts)
              ListTile(
                contentPadding: EdgeInsets.zero,
                dense: true,
                leading: const Icon(Icons.description_outlined),
                title: Text(text),
                trailing: const Icon(Icons.open_in_new, size: 18),
                onTap: () => _openText(text),
              ),
          ],
          const SizedBox(height: 20),
          Text(
            'Screenshots, demos & saves',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 8),
          if (files.isEmpty && !_loading)
            const Text(
              'Import files to keep screenshots, demos and saves with this entry.',
            ),
          for (final file in files)
            Card(
              margin: const EdgeInsets.symmetric(vertical: 4),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (rowId(file, 'FileTypeID') == 1 &&
                      rowText(file, 'path').isNotEmpty)
                    InkWell(
                      onTap: () =>
                          openLocalFile(context, rowText(file, 'path')),
                      child: ConstrainedBox(
                        constraints: const BoxConstraints(maxHeight: 160),
                        child: Image.file(
                          File(rowText(file, 'path')),
                          fit: BoxFit.contain,
                          errorBuilder: (_, _, _) => const SizedBox.shrink(),
                        ),
                      ),
                    ),
                  ListTile(
                    dense: true,
                    contentPadding: const EdgeInsets.only(left: 12, right: 4),
                    title: Text(
                      rowText(file, 'FileName'),
                      overflow: TextOverflow.ellipsis,
                    ),
                    subtitle: file['pathError'] != null
                        ? Text(
                            '${file['pathError']}',
                            style: TextStyle(
                              color: Theme.of(context).colorScheme.error,
                            ),
                          )
                        : Text(switch (rowId(file, 'FileTypeID')) {
                            1 => 'Screenshot',
                            2 => 'Demo',
                            3 => 'Saved game',
                            6 => 'Artwork',
                            _ => 'Associated file',
                          }),
                    trailing: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        if (rowId(file, 'FileTypeID') == 2)
                          IconButton(
                            tooltip: 'Replay demo',
                            icon: const Icon(Icons.play_arrow),
                            onPressed:
                                game['pathError'] != null ||
                                    file['pathError'] != null ||
                                    rowText(file, 'path').isEmpty
                                ? null
                                : () => showPlayEntry(
                                    context,
                                    widget.controller,
                                    game,
                                    demoPath: rowText(file, 'path'),
                                  ),
                          ),
                        IconButton(
                          tooltip: 'Open',
                          icon: const Icon(Icons.open_in_new),
                          onPressed:
                              file['pathError'] != null ||
                                  rowText(file, 'path').isEmpty
                              ? null
                              : () => openLocalFile(
                                  context,
                                  rowText(file, 'path'),
                                ),
                        ),
                        IconButton(
                          tooltip: 'Remove',
                          icon: const Icon(Icons.delete_outline),
                          onPressed: () => _remove(file),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          const SizedBox(height: 8),
          OutlinedButton.icon(
            onPressed: _import,
            icon: const Icon(Icons.add_photo_alternate_outlined),
            label: const Text('Import screenshot, save or demo'),
          ),
          const SizedBox(height: 20),
          Text(
            'Level statistics',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          if (stats.isEmpty)
            const Padding(
              padding: EdgeInsets.only(top: 8),
              child: Text(
                'Recorded level statistics appear here after a supported source-port session.',
              ),
            ),
          for (final stat in stats)
            ListTile(
              contentPadding: EdgeInsets.zero,
              dense: true,
              title: Text(
                '${stat['MapName'] ?? 'Map'} · ${_duration(stat['LevelTime'])}',
              ),
              subtitle: Text(
                'Kills ${stat['KillCount'] ?? 0}/${stat['TotalKills'] ?? 0} · Secrets ${stat['SecretCount'] ?? 0}/${stat['TotalSecrets'] ?? 0}',
              ),
            ),
        ],
      ),
    );
  }

  String _duration(dynamic value) {
    final seconds = (value as num?)?.round() ?? 0;
    return '${seconds ~/ 60}:${(seconds % 60).toString().padLeft(2, '0')}';
  }
}
