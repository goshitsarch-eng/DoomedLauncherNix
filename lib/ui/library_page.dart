import 'dart:io';
import 'package:flutter/services.dart';
import 'package:flutter/material.dart';
import '../state/app_controller.dart';
import 'details_panel.dart';
import 'edit_dialog.dart';
import 'play_dialog.dart';
import 'widgets/common.dart';

class LibraryFilter {
  const LibraryFilter(this.id, this.label, {this.tagId});
  final String id, label;
  final int? tagId;
}

// Legacy SQLite timestamps have no zone and represent local time. ISO values
// may carry UTC or an offset; compare instants, not their different spellings.
int _compareLibraryDates(String left, String right) {
  final leftDate = DateTime.tryParse(left.trim())?.toUtc();
  final rightDate = DateTime.tryParse(right.trim())?.toUtc();
  if (leftDate == null) return rightDate == null ? 0 : -1;
  if (rightDate == null) return 1;
  return leftDate.compareTo(rightDate);
}

List<Map<String, dynamic>> filterLibrary(
  List<Map<String, dynamic>> games, {
  String query = '',
  String filter = 'local',
  int? tagId,
  String sort = 'Title',
  bool descending = false,
}) {
  var rows = games.where((game) {
    final isIwad = game['isIwad'] == true;
    if (filter == 'iwads' && !isIwad) return false;
    if (filter != 'iwads' && tagId == null && isIwad) return false;
    final tags = game['tagIds'] as List? ?? [];
    if (filter == 'untagged' && tags.isNotEmpty) return false;
    if (tagId != null && !tags.contains(tagId)) return false;
    return true;
  }).toList();
  final search = query.trim().toLowerCase();
  if (search.isNotEmpty) {
    rows = rows
        .where(
          (game) => [
            'Title',
            'Author',
            'FileName',
            'Description',
          ].any((key) => rowText(game, key).toLowerCase().contains(search)),
        )
        .toList();
  }
  if (filter == 'recent') {
    rows.sort((a, b) {
      final order = _compareLibraryDates(
        rowText(b, 'Downloaded'),
        rowText(a, 'Downloaded'),
      );
      return order == 0 ? rowId(a).compareTo(rowId(b)) : order;
    });
    rows = rows.take(25).toList();
  }
  rows.sort((a, b) {
    final numeric = sort == 'Rating' || sort == 'MinutesPlayed';
    final date = const {
      'LastPlayed',
      'Downloaded',
      'ReleaseDate',
    }.contains(sort);
    final left = sort == 'Title' ? gameTitle(a) : rowText(a, sort);
    final right = sort == 'Title' ? gameTitle(b) : rowText(b, sort);
    final order = numeric
        ? (double.tryParse(left) ?? 0).compareTo(double.tryParse(right) ?? 0)
        : date
        ? _compareLibraryDates(left, right)
        : left.toLowerCase().compareTo(right.toLowerCase());
    return order == 0
        ? rowId(a).compareTo(rowId(b))
        : descending
        ? -order
        : order;
  });
  return rows;
}

class LibraryPage extends StatefulWidget {
  const LibraryPage({
    super.key,
    required this.controller,
    required this.onImportFiles,
    required this.onImportDirectory,
  });
  final AppController controller;
  final VoidCallback onImportFiles, onImportDirectory;
  @override
  State<LibraryPage> createState() => LibraryPageState();
}

class LibraryPageState extends State<LibraryPage> {
  final _search = TextEditingController();
  final _searchFocus = FocusNode();
  final _libraryFocus = FocusNode(debugLabel: 'Library entries');
  String _filter = 'local', _sort = 'Title';
  int? _tagId, _selectedId;
  bool _descending = false, _details = true;
  bool _restored = false;
  List<Map<String, dynamic>> get _visibleRows => filterLibrary(
    widget.controller.games,
    query: _search.text,
    filter: _filter,
    tagId: _tagId,
    sort: _sort,
    descending: _descending,
  );
  Map<String, dynamic>? get selected =>
      _visibleRows.where((g) => rowId(g) == _selectedId).firstOrNull;
  void focusSearch() => _searchFocus.requestFocus();
  Future<void> playSelected({bool withOptions = false}) async {
    final game = selected;
    if (game != null && game['pathError'] == null) {
      await showPlayEntry(
        context,
        widget.controller,
        game,
        forceOptions: withOptions,
      );
    }
  }

  Future<void> deleteSelected() async {
    if (selected != null) await _delete(selected!);
  }

  Future<void> editSelected() async {
    if (selected != null) {
      await showEditEntry(context, widget.controller, selected!);
    }
  }

  @override
  void dispose() {
    _search.dispose();
    _searchFocus.dispose();
    _libraryFocus.dispose();
    super.dispose();
  }

  bool _viewEnabled(String view) =>
      '${widget.controller.settings['VisibleViews'] ?? 'Recent;Untagged;IWads;Id Games'}'
          .split(';')
          .contains(view);
  List<LibraryFilter> get _filters => [
    const LibraryFilter('local', 'All mods'),
    if (_viewEnabled('Recent')) const LibraryFilter('recent', 'Recent'),
    if (_viewEnabled('Untagged')) const LibraryFilter('untagged', 'Untagged'),
    if (_viewEnabled('IWads')) const LibraryFilter('iwads', 'IWADs'),
    for (final tag in widget.controller.tags)
      if (tag['has_tab'] == true || tag['has_tab'] == 1)
        LibraryFilter(
          'tag-${tag['tag_id']}',
          rowText(tag, 'name'),
          tagId: rowId(tag, 'tag_id'),
        ),
  ];
  Future<void> _changeFilter(LibraryFilter filter, int index) async {
    setState(() {
      _filter = filter.id;
      _tagId = filter.tagId;
      _selectedId = null;
    });
    await widget.controller.execute('update_settings', {
      'values': {'LastSelectedTabIndex': '$index'},
    });
  }

  Future<void> _delete(Map<String, dynamic> game) async {
    final result = await showDialog<bool>(
      context: context,
      builder: (context) => _DeleteEntryDialog(game: game),
    );
    if (result == null) return;
    await widget.controller.execute('delete_entry', {
      'id': rowId(game),
      'delete_file': result,
    });
    if (mounted && widget.controller.error == null) {
      setState(() => _selectedId = null);
    }
  }

  Future<void> _rename(Map<String, dynamic> game) async {
    await showDialog<void>(
      context: context,
      builder: (_) => _RenameEntryDialog(
        name: rowText(game, 'FileName'),
        onRename: (name) async {
          if (name == rowText(game, 'FileName')) return null;
          final result = await widget.controller.execute('rename_entry', {
            'id': rowId(game),
            'name': name,
          });
          return result == null
              ? widget.controller.error ?? 'Could not rename the file.'
              : null;
        },
      ),
    );
  }

  Future<void> _action(String action, Map<String, dynamic> game) async {
    if (game['pathError'] != null &&
        [
          'play',
          'options',
          'open',
          'text',
          'resync',
          'promote',
        ].contains(action)) {
      showMessage(context, '${game['pathError']}');
      return;
    }
    switch (action) {
      case 'play':
        await showPlayEntry(
          context,
          widget.controller,
          game,
          forceOptions: false,
        );
      case 'options':
        await showPlayEntry(context, widget.controller, game);
      case 'details':
        await showEntryDetails(context, widget.controller, game);
      case 'edit':
        await showEditEntry(context, widget.controller, game);
      case 'rename':
        await _rename(game);
      case 'delete':
        await _delete(game);
      case 'resync':
        await widget.controller.execute('resync_metadata', {
          'ids': [rowId(game)],
        });
      case 'open':
        if (mounted) await openLocalFile(context, rowText(game, 'path'));
      case 'folder':
        await openLocalFile(context, File(rowText(game, 'path')).parent.path);
      case 'copy':
        await Clipboard.setData(ClipboardData(text: rowText(game, 'path')));
        if (mounted) showMessage(context, 'Path copied');
      case 'text':
        final result = await widget.controller.execute('open_text', {
          'id': rowId(game),
        });
        if (mounted && result != null) {
          await openLocalFile(context, '${result['path']}');
        }
      case 'promote':
        await widget.controller.execute('import_files', {
          'paths': [rowText(game, 'path')],
          'as_iwads': true,
        });
    }
  }

  List<PopupMenuEntry<String>> _menuItems(Map<String, dynamic> game) => [
    PopupMenuItem(
      value: 'play',
      enabled: game['pathError'] == null,
      child: const Text('Play'),
    ),
    PopupMenuItem(
      value: 'options',
      enabled: game['pathError'] == null,
      child: const Text('Play with options…'),
    ),
    const PopupMenuItem(value: 'details', child: Text('Details & statistics')),
    const PopupMenuDivider(),
    const PopupMenuItem(value: 'edit', child: Text('Edit metadata…')),
    PopupMenuItem(
      value: 'rename',
      enabled: game['pathError'] == null,
      child: const Text('Rename file…'),
    ),
    PopupMenuItem(
      value: 'resync',
      enabled: game['pathError'] == null,
      child: const Text('Resync metadata'),
    ),
    if (game['isIwad'] != true)
      PopupMenuItem(
        value: 'promote',
        enabled: game['pathError'] == null,
        child: Text('Use as a base game (IWAD)'),
      ),
    const PopupMenuDivider(),
    PopupMenuItem(
      value: 'text',
      enabled: game['pathError'] == null,
      child: const Text('Open text file'),
    ),
    PopupMenuItem(
      value: 'open',
      enabled: game['pathError'] == null && rowText(game, 'path').isNotEmpty,
      child: const Text('Open file externally'),
    ),
    PopupMenuItem(
      value: 'folder',
      enabled: rowText(game, 'path').isNotEmpty,
      child: const Text('Show in folder'),
    ),
    PopupMenuItem(
      value: 'copy',
      enabled: rowText(game, 'path').isNotEmpty,
      child: const Text('Copy file path'),
    ),
    const PopupMenuDivider(),
    const PopupMenuItem(value: 'delete', child: Text('Delete…')),
  ];
  void _selectGame(Map<String, dynamic> game) {
    _libraryFocus.requestFocus();
    setState(() => _selectedId = rowId(game));
  }

  Future<void> _contextMenu(
    TapDownDetails details,
    Map<String, dynamic> game,
  ) async {
    _selectGame(game);
    final overlay = Overlay.of(context).context.findRenderObject() as RenderBox;
    final action = await showMenu<String>(
      context: context,
      position: RelativeRect.fromRect(
        Rect.fromLTWH(
          details.globalPosition.dx,
          details.globalPosition.dy,
          1,
          1,
        ),
        Offset.zero & overlay.size,
      ),
      items: _menuItems(game),
    );
    if (mounted && action != null) await _action(action, game);
  }

  @override
  Widget build(BuildContext context) {
    final filters = _filters;
    if (!_restored && widget.controller.snapshot.isNotEmpty) {
      _restored = true;
      final index =
          int.tryParse(
            '${widget.controller.settings['LastSelectedTabIndex']}',
          ) ??
          0;
      if (index >= 0 && index < filters.length) {
        _filter = filters[index].id;
        _tagId = filters[index].tagId;
      }
    }
    if (!filters.any((f) => f.id == _filter)) {
      _filter = 'local';
      _tagId = null;
    }
    final rows = _visibleRows;
    if (!rows.any((game) => rowId(game) == _selectedId)) _selectedId = null;
    final game = rows.where((game) => rowId(game) == _selectedId).firstOrNull;
    final tiles = widget.controller.settings['GameFileViewType'] == 'TileView';
    final content = LayoutBuilder(
      builder: (context, constraints) {
        final sideDetails =
            _details && game != null && constraints.maxWidth > 900;
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(24, 22, 24, 16),
              child: Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Your library',
                          style: Theme.of(context).textTheme.headlineMedium,
                        ),
                        const SizedBox(height: 4),
                        Text(
                          '${widget.controller.games.length} files · ready for your next run',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                  FilledButton.icon(
                    onPressed: widget.onImportFiles,
                    icon: const Icon(Icons.add),
                    label: const Text('Import'),
                  ),
                  const SizedBox(width: 6),
                  PopupMenuButton<String>(
                    tooltip: 'Import options',
                    onSelected: (value) => value == 'directory'
                        ? widget.onImportDirectory()
                        : widget.onImportFiles(),
                    itemBuilder: (_) => const [
                      PopupMenuItem(
                        value: 'files',
                        child: Text('Import files…'),
                      ),
                      PopupMenuItem(
                        value: 'directory',
                        child: Text('Import directory…'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 24),
              child: TextField(
                controller: _search,
                focusNode: _searchFocus,
                onChanged: (_) => setState(() {}),
                decoration: InputDecoration(
                  hintText: 'Search title, author or filename',
                  prefixIcon: const Icon(Icons.search),
                  suffixIcon: _search.text.isEmpty
                      ? null
                      : IconButton(
                          tooltip: 'Clear search',
                          icon: const Icon(Icons.close),
                          onPressed: () {
                            _search.clear();
                            setState(() {});
                          },
                        ),
                ),
              ),
            ),
            const SizedBox(height: 12),
            SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 24),
              child: Row(
                children: [
                  for (var i = 0; i < filters.length; i++)
                    Padding(
                      padding: const EdgeInsets.only(right: 8),
                      child: ChoiceChip(
                        label: Text(filters[i].label),
                        selected: _filter == filters[i].id,
                        onSelected: (_) => _changeFilter(filters[i], i),
                      ),
                    ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 12, 4),
              child: Wrap(
                crossAxisAlignment: WrapCrossAlignment.center,
                spacing: 4,
                children: [
                  FilledButton.tonalIcon(
                    onPressed: game == null || game['pathError'] != null
                        ? null
                        : () => playSelected(),
                    icon: const Icon(Icons.play_arrow),
                    label: const Text('Play'),
                  ),
                  TextButton(
                    onPressed: game == null || game['pathError'] != null
                        ? null
                        : () => playSelected(withOptions: true),
                    child: const Text('Options'),
                  ),
                  IconButton(
                    tooltip: 'Details',
                    isSelected: _details,
                    icon: const Icon(Icons.vertical_split_outlined),
                    selectedIcon: const Icon(Icons.vertical_split),
                    onPressed: game == null
                        ? null
                        : () {
                            if (constraints.maxWidth <= 900) {
                              showEntryDetails(
                                context,
                                widget.controller,
                                game,
                              );
                            } else {
                              setState(() => _details = !_details);
                            }
                          },
                  ),
                  PopupMenuButton<String>(
                    tooltip: 'Sort library',
                    icon: const Icon(Icons.sort),
                    onSelected: (value) => setState(() => _sort = value),
                    itemBuilder: (_) =>
                        <String, String>{
                              'Title': 'Title',
                              'Author': 'Author',
                              'FileName': 'Filename',
                              'Rating': 'Rating',
                              'LastPlayed': 'Last played',
                              'Downloaded': 'Imported',
                              'MinutesPlayed': 'Play time',
                            }.entries
                            .map(
                              (entry) => CheckedPopupMenuItem(
                                value: entry.key,
                                checked: _sort == entry.key,
                                child: Text(entry.value),
                              ),
                            )
                            .toList(),
                  ),
                  IconButton(
                    tooltip: _descending
                        ? 'Descending order'
                        : 'Ascending order',
                    icon: Icon(_descending ? Icons.south : Icons.north),
                    onPressed: () => setState(() => _descending = !_descending),
                  ),
                  IconButton(
                    tooltip: tiles ? 'Show list view' : 'Show tile view',
                    icon: Icon(
                      tiles ? Icons.view_list_outlined : Icons.grid_view,
                    ),
                    onPressed: () =>
                        widget.controller.execute('update_settings', {
                          'values': {
                            'GameFileViewType': tiles ? 'GridView' : 'TileView',
                          },
                        }),
                  ),
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                    child: Text(
                      '${rows.length} shown',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ),
                ],
              ),
            ),
            const Divider(height: 1),
            Expanded(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Expanded(
                    child: rows.isEmpty
                        ? _emptyState()
                        : tiles
                        ? _grid(rows)
                        : _list(rows),
                  ),
                  if (sideDetails) ...[
                    const VerticalDivider(width: 1),
                    SizedBox(
                      width: 350,
                      child: EntryDetailsPanel(
                        key: ValueKey(_selectedId),
                        controller: widget.controller,
                        game: game,
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ],
        );
      },
    );
    return Focus(focusNode: _libraryFocus, child: content);
  }

  Widget _emptyState() => Center(
    child: SingleChildScrollView(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              _search.text.isNotEmpty
                  ? Icons.search_off
                  : Icons.sports_esports_outlined,
              size: 58,
              color: Theme.of(context).colorScheme.primary,
            ),
            const SizedBox(height: 16),
            Text(
              _search.text.isNotEmpty
                  ? 'No matching files'
                  : 'Make room for another adventure',
              style: Theme.of(context).textTheme.titleLarge,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 8),
            Text(
              _search.text.isNotEmpty
                  ? 'Try another title, author or filename.'
                  : 'Import your WADs, mods and map packs. Drop files or folders anywhere in the window.',
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 18),
            OutlinedButton.icon(
              onPressed: widget.onImportFiles,
              icon: const Icon(Icons.add),
              label: const Text('Import files'),
            ),
          ],
        ),
      ),
    ),
  );
  Widget _gameMenu(Map<String, dynamic> game) => PopupMenuButton<String>(
    tooltip: 'Actions for ${gameTitle(game)}',
    onSelected: (value) => _action(value, game),
    itemBuilder: (_) => _menuItems(game),
  );

  Widget _list(List<Map<String, dynamic>> rows) => ListView.builder(
    padding: const EdgeInsets.all(12),
    itemCount: rows.length,
    itemBuilder: (context, index) {
      final game = rows[index], selected = rowId(rows[index]) == _selectedId;
      return Semantics(
        selected: selected,
        child: GestureDetector(
          onSecondaryTapDown: (details) => _contextMenu(details, game),
          child: Card(
            elevation: 0,
            color: selected
                ? Theme.of(context).colorScheme.secondaryContainer
                : null,
            margin: const EdgeInsets.symmetric(vertical: 2),
            child: Row(
              children: [
                Expanded(
                  child: GestureDetector(
                    onDoubleTap: () => _action('play', game),
                    child: ListTile(
                      selected: selected,
                      onTap: () => _selectGame(game),
                      leading: ClipRRect(
                        borderRadius: BorderRadius.circular(6),
                        child: SizedBox(
                          width: 64,
                          height: 48,
                          child: GameArtwork(game: game),
                        ),
                      ),
                      title: Text(
                        gameTitle(game),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                      subtitle: Text(
                        [
                          rowText(game, 'Author'),
                          rowText(game, 'FileName'),
                        ].where((t) => t.isNotEmpty).join(' · '),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                  ),
                ),
                if (game['pathError'] != null)
                  Tooltip(
                    message: '${game['pathError']}',
                    child: const Icon(Icons.warning_amber_rounded, size: 20),
                  ),
                if ((game['Rating'] as num? ?? 0) > 0)
                  Padding(
                    padding: const EdgeInsets.only(left: 8),
                    child: Text(
                      '★ ${game['Rating']}',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ),
                _gameMenu(game),
              ],
            ),
          ),
        ),
      );
    },
  );

  Widget _grid(List<Map<String, dynamic>> rows) => GridView.builder(
    padding: const EdgeInsets.all(16),
    gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
      maxCrossAxisExtent: 240,
      mainAxisExtent: 224,
      mainAxisSpacing: 12,
      crossAxisSpacing: 12,
    ),
    itemCount: rows.length,
    itemBuilder: (context, index) {
      final game = rows[index], selected = rowId(rows[index]) == _selectedId;
      void select() => _selectGame(game);
      return Semantics(
        selected: selected,
        child: GestureDetector(
          onSecondaryTapDown: (details) => _contextMenu(details, game),
          child: Card(
            clipBehavior: Clip.antiAlias,
            color: selected
                ? Theme.of(context).colorScheme.secondaryContainer
                : null,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(12),
              side: BorderSide(
                color: selected
                    ? Theme.of(context).colorScheme.primary
                    : Colors.transparent,
                width: 2,
              ),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Expanded(
                  child: GestureDetector(
                    onDoubleTap: () => _action('play', game),
                    child: InkWell(
                      onTap: select,
                      child: Stack(
                        fit: StackFit.expand,
                        children: [
                          GameArtwork(game: game),
                          if (game['pathError'] != null)
                            Positioned(
                              top: 4,
                              right: 4,
                              child: Tooltip(
                                message: '${game['pathError']}',
                                child: const Chip(
                                  avatar: Icon(
                                    Icons.warning_amber_rounded,
                                    size: 18,
                                  ),
                                  label: Text('Unavailable'),
                                ),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(12, 10, 4, 10),
                  child: Row(
                    children: [
                      Expanded(
                        child: GestureDetector(
                          onDoubleTap: () => _action('play', game),
                          child: InkWell(
                            onTap: select,
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  gameTitle(game),
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: Theme.of(context).textTheme.titleSmall,
                                ),
                                const SizedBox(height: 4),
                                Text(
                                  rowText(game, 'Author').isEmpty
                                      ? rowText(game, 'FileName')
                                      : rowText(game, 'Author'),
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: Theme.of(context).textTheme.bodySmall,
                                ),
                              ],
                            ),
                          ),
                        ),
                      ),
                      _gameMenu(game),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      );
    },
  );
}

class _DeleteEntryDialog extends StatefulWidget {
  const _DeleteEntryDialog({required this.game});
  final Map<String, dynamic> game;
  @override
  State<_DeleteEntryDialog> createState() => _DeleteEntryDialogState();
}

class _DeleteEntryDialogState extends State<_DeleteEntryDialog> {
  bool _deleteFile = false;
  @override
  Widget build(BuildContext context) => AlertDialog(
    scrollable: true,
    title: const Text('Delete library entry?'),
    content: SizedBox(
      width: 440,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Remove ${gameTitle(widget.game)} from your library? Metadata, tags, statistics and associations for this entry will be removed.',
          ),
          const SizedBox(height: 12),
          CheckboxListTile(
            contentPadding: EdgeInsets.zero,
            title: const Text('Also delete the managed game file'),
            value: _deleteFile,
            onChanged: (value) => setState(() => _deleteFile = value ?? false),
          ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: () => Navigator.pop(context, _deleteFile),
        child: const Text('Delete'),
      ),
    ],
  );
}

class _RenameEntryDialog extends StatefulWidget {
  const _RenameEntryDialog({required this.name, required this.onRename});
  final String name;
  final Future<String?> Function(String) onRename;
  @override
  State<_RenameEntryDialog> createState() => _RenameEntryDialogState();
}

class _RenameEntryDialogState extends State<_RenameEntryDialog> {
  late final _name = TextEditingController(text: widget.name);
  bool _saving = false;
  String? _error;

  Future<void> _save() async {
    final name = _name.text.trim();
    if (_saving || name.isEmpty) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    final error = await widget.onRename(name);
    if (!mounted) return;
    if (error == null) {
      Navigator.pop(context);
    } else {
      setState(() {
        _saving = false;
        _error = error;
      });
    }
  }

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_saving,
    child: AlertDialog(
      title: const Text('Rename file'),
      content: SizedBox(
        width: 440,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              TextField(
                autofocus: true,
                enabled: !_saving,
                controller: _name,
                decoration: const InputDecoration(
                  labelText: 'Filename including extension',
                ),
                onChanged: (_) => setState(() {}),
                onSubmitted: (_) => _save(),
              ),
              if (_error != null) ...[
                const SizedBox(height: 12),
                InlineError(_error!),
              ],
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
          onPressed: _saving || _name.text.trim().isEmpty ? null : _save,
          child: Text(_saving ? 'Renaming…' : 'Rename'),
        ),
      ],
    ),
  );
}
