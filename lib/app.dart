import 'dart:async';
import 'dart:io';
import 'package:desktop_drop/desktop_drop.dart';
import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:window_manager/window_manager.dart';
import 'package:flutter/services.dart';
import 'state/app_controller.dart';
import 'ui/idgames.dart';
import 'ui/library_page.dart';
import 'ui/management.dart';
import 'ui/play_dialog.dart';
import 'ui/widgets/common.dart';

final launcherNavigatorKey = GlobalKey<NavigatorState>();

class DoomLauncherApp extends StatelessWidget {
  const DoomLauncherApp({super.key, required this.controller});
  final AppController controller;
  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: controller,
    builder: (context, _) => MaterialApp(
      navigatorKey: launcherNavigatorKey,
      title: 'Doom Launcher',
      debugShowCheckedModeBanner: false,
      themeMode: switch (controller.settings['ColorThemeType']) {
        'Dark' => ThemeMode.dark,
        'Default' => ThemeMode.light,
        _ => ThemeMode.system,
      },
      theme: _theme(Brightness.light),
      darkTheme: _theme(Brightness.dark),
      home: LauncherHome(controller: controller),
    ),
  );
  ThemeData _theme(Brightness brightness) => ThemeData(
    useMaterial3: true,
    brightness: brightness,
    colorSchemeSeed: const Color(0xffb35c30),
    visualDensity: VisualDensity.compact,
    inputDecorationTheme: const InputDecorationTheme(
      border: OutlineInputBorder(),
      isDense: true,
    ),
    tooltipTheme: const TooltipThemeData(
      waitDuration: Duration(milliseconds: 450),
    ),
  );
}

class LauncherHome extends StatefulWidget {
  const LauncherHome({super.key, required this.controller});
  final AppController controller;
  @override
  State<LauncherHome> createState() => _LauncherHomeState();
}

class _LauncherHomeState extends State<LauncherHome> {
  final _libraryKey = GlobalKey<LibraryPageState>();
  int _page = 0;
  final _visited = <int>{0};
  bool _dragging = false;
  Timer? _sessionsTimer;
  bool _polling = false;
  bool get _canPlaySelected {
    final game = _libraryKey.currentState?.selected;
    return game != null && game['pathError'] == null;
  }

  @override
  void initState() {
    super.initState();
    _sessionsTimer = Timer.periodic(const Duration(seconds: 2), (_) async {
      if (widget.controller.shutdownInProgress ||
          _polling ||
          !widget.controller.sessions.any(
            (s) => s['status'] == 'running' || s['status'] == 'starting',
          )) {
        return;
      }
      _polling = true;
      try {
        await widget.controller.refresh();
      } catch (error) {
        if (mounted) widget.controller.reportError(error);
      } finally {
        _polling = false;
      }
    });
  }

  @override
  void dispose() {
    _sessionsTimer?.cancel();
    super.dispose();
  }

  void _navigate(int page) {
    if (widget.controller.shutdownInProgress) return;
    setState(() {
      _page = page;
      _visited.add(page);
    });
  }

  Future<void> _pickFiles({bool asIwads = false}) async {
    if (widget.controller.shutdownInProgress) return;
    try {
      final files = await openFiles(
        acceptedTypeGroups: const [
          XTypeGroup(
            label: 'Doom content',
            extensions: ['wad', 'zip', 'pk3', 'pk7', 'ipk3', 'deh', 'bex'],
          ),
          XTypeGroup(label: 'All files'),
        ],
      );
      if (files.isNotEmpty) {
        await _importPaths(files.map((f) => f.path).toList(), asIwads: asIwads);
      }
    } catch (error) {
      if (mounted) showMessage(context, '$error');
    }
  }

  Future<void> _pickDirectory() async {
    if (widget.controller.shutdownInProgress) return;
    try {
      final path = await getDirectoryPath(
        confirmButtonText: 'Import directory',
      );
      if (path != null) await _importPaths([path]);
    } catch (error) {
      if (mounted) showMessage(context, '$error');
    }
  }

  Future<void> _importPaths(List<String> paths, {bool asIwads = false}) async {
    if (widget.controller.shutdownInProgress) return;
    final files = <String>[], failures = <String>[];
    var added = 0;
    for (final path in paths) {
      final type = await FileSystemEntity.type(path);
      if (type == FileSystemEntityType.directory) {
        final result = await widget.controller.execute('import_directory', {
          'path': path,
          'recursive': true,
          'as_iwads': asIwads,
        });
        if (result != null) {
          added += (result['added'] as List? ?? []).length;
          failures.addAll(
            (result['failed'] as List? ?? []).map(
              (e) => '${e['path']}: ${e['message']}',
            ),
          );
        }
      } else {
        files.add(path);
      }
    }
    if (files.isNotEmpty) {
      final result = await widget.controller.execute('import_files', {
        'paths': files,
        'as_iwads': asIwads,
      });
      if (result != null) {
        added += (result['added'] as List? ?? []).length;
        failures.addAll(
          (result['failed'] as List? ?? []).map(
            (e) => '${e['path']}: ${e['message']}',
          ),
        );
      }
    }
    if (!mounted) return;
    if (failures.isNotEmpty) {
      await showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          title: Text('Imported $added files'),
          content: SizedBox(
            width: 580,
            child: SingleChildScrollView(
              child: SelectableText(failures.join('\n\n')),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Close'),
            ),
          ],
        ),
      );
    } else if (widget.controller.error == null) {
      showMessage(context, 'Imported $added ${added == 1 ? 'file' : 'files'}');
    }
    _navigate(0);
  }

  void _about() => showAboutDialog(
    context: context,
    applicationName: 'Doom Launcher',
    applicationVersion:
        '${widget.controller.snapshot['version'] ?? '5.0.0-alpha.1'}',
    applicationIcon: Image.asset(
      'data/com.goshapps.DoomLauncher.png',
      width: 64,
      height: 64,
    ),
    applicationLegalese: '© Doom Launcher contributors · GPL-3.0',
    children: const [
      SizedBox(height: 12),
      Text('Your Doom library, source ports, mods and memories in one place.'),
      SizedBox(height: 8),
      SelectableText('https://github.com/goshitsarch-eng/DoomedLauncherNix'),
    ],
  );
  Future<void> _scanStores() => showDialog<void>(
    context: context,
    builder: (_) => StoreScanDialog(controller: widget.controller),
  );
  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final showIdgames =
        '${controller.settings['VisibleViews'] ?? 'Recent;Untagged;IWads;Id Games'}'
            .split(';')
            .contains('Id Games');
    final pageIds = [0, if (showIdgames) 1, 2, 3, 4];
    final initialLoading = controller.snapshot.isEmpty && controller.busy;
    final needsSetup = controller.ports.isEmpty || controller.iwads.isEmpty;
    final content = CallbackShortcuts(
      bindings: {
        const SingleActivator(LogicalKeyboardKey.keyO, control: true): () =>
            _pickFiles(),
        const SingleActivator(
          LogicalKeyboardKey.keyO,
          control: true,
          shift: true,
        ): _pickDirectory,
        const SingleActivator(LogicalKeyboardKey.keyF, control: true): () {
          _navigate(0);
          WidgetsBinding.instance.addPostFrameCallback(
            (_) => _libraryKey.currentState?.focusSearch(),
          );
        },
        const SingleActivator(LogicalKeyboardKey.keyO, meta: true): () =>
            _pickFiles(),
        const SingleActivator(LogicalKeyboardKey.keyO, meta: true, shift: true):
            _pickDirectory,
        const SingleActivator(LogicalKeyboardKey.keyP, meta: true): () =>
            _libraryKey.currentState?.playSelected(),
        const SingleActivator(LogicalKeyboardKey.keyE, meta: true): () =>
            _libraryKey.currentState?.editSelected(),
        const SingleActivator(LogicalKeyboardKey.keyF, meta: true): () {
          _navigate(0);
          WidgetsBinding.instance.addPostFrameCallback(
            (_) => _libraryKey.currentState?.focusSearch(),
          );
        },
        const SingleActivator(LogicalKeyboardKey.f5): () =>
            controller.execute('snapshot'),
        const SingleActivator(LogicalKeyboardKey.keyP, control: true): () =>
            _libraryKey.currentState?.playSelected(),
        const SingleActivator(LogicalKeyboardKey.keyE, control: true): () =>
            _libraryKey.currentState?.editSelected(),
      },
      child: Focus(
        autofocus: true,
        child: Scaffold(
          bottomNavigationBar: controller.sessions.isNotEmpty
              ? _sessionBar()
              : null,
          body: DropTarget(
            enable:
                !controller.shutdownInProgress &&
                (ModalRoute.of(context)?.isCurrent ?? true),
            onDragEntered: (_) => setState(() => _dragging = true),
            onDragExited: (_) => setState(() => _dragging = false),
            onDragDone: (details) {
              setState(() => _dragging = false);
              _importPaths(details.files.map((f) => f.path).toList());
            },
            child: Stack(
              children: [
                Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (defaultTargetPlatform != TargetPlatform.macOS)
                      _menuBar(),
                    if (controller.busy)
                      const LinearProgressIndicator(minHeight: 2)
                    else
                      const SizedBox(height: 2),
                    if (controller.error != null)
                      MaterialBanner(
                        content: SelectableText(controller.error!),
                        leading: const Icon(Icons.error_outline),
                        actions: [
                          if (controller.snapshot.isEmpty)
                            TextButton(
                              onPressed: controller.initialize,
                              child: const Text('Retry startup'),
                            ),
                          TextButton(
                            onPressed: controller.clearError,
                            child: const Text('Dismiss'),
                          ),
                        ],
                      ),
                    Expanded(
                      child: initialLoading
                          ? const Center(child: CircularProgressIndicator())
                          : LayoutBuilder(
                              builder: (context, constraints) {
                                final compact = constraints.maxWidth < 850;
                                return Row(
                                  crossAxisAlignment:
                                      CrossAxisAlignment.stretch,
                                  children: [
                                    NavigationRail(
                                      selectedIndex: pageIds
                                          .indexOf(_page)
                                          .clamp(0, pageIds.length - 1),
                                      extended: !compact,
                                      minExtendedWidth: 205,
                                      onDestinationSelected: (index) =>
                                          _navigate(pageIds[index]),
                                      leading: Padding(
                                        padding: const EdgeInsets.fromLTRB(
                                          8,
                                          14,
                                          8,
                                          22,
                                        ),
                                        child: compact
                                            ? Image.asset(
                                                'data/com.goshapps.DoomLauncher.png',
                                                width: 38,
                                                height: 38,
                                              )
                                            : Row(
                                                mainAxisSize: MainAxisSize.min,
                                                children: [
                                                  Image.asset(
                                                    'data/com.goshapps.DoomLauncher.png',
                                                    width: 38,
                                                    height: 38,
                                                  ),
                                                  const SizedBox(width: 10),
                                                  const Text(
                                                    'DOOM\nLAUNCHER',
                                                    style: TextStyle(
                                                      fontWeight:
                                                          FontWeight.w800,
                                                      letterSpacing: 1.2,
                                                      height: 1.15,
                                                    ),
                                                  ),
                                                ],
                                              ),
                                      ),
                                      labelType: compact
                                          ? NavigationRailLabelType.all
                                          : NavigationRailLabelType.none,
                                      destinations: [
                                        NavigationRailDestination(
                                          icon: Icon(Icons.games_outlined),
                                          selectedIcon: Icon(Icons.games),
                                          label: Text('Library'),
                                        ),
                                        if (showIdgames)
                                          NavigationRailDestination(
                                            icon: Icon(Icons.travel_explore),
                                            label: Text('Get mods'),
                                          ),
                                        NavigationRailDestination(
                                          icon: Icon(Icons.memory_outlined),
                                          label: Text('Source ports'),
                                        ),
                                        NavigationRailDestination(
                                          icon: Icon(Icons.sell_outlined),
                                          label: Text('Tags'),
                                        ),
                                        NavigationRailDestination(
                                          icon: Icon(Icons.settings_outlined),
                                          label: Text('Settings'),
                                        ),
                                      ],
                                    ),
                                    const VerticalDivider(width: 1),
                                    Expanded(
                                      child: Column(
                                        children: [
                                          if (needsSetup &&
                                              _page == 0 &&
                                              controller.snapshot.isNotEmpty)
                                            _setupBanner(),
                                          Expanded(
                                            child: IndexedStack(
                                              index: _page,
                                              children: [
                                                LibraryPage(
                                                  key: _libraryKey,
                                                  controller: controller,
                                                  onImportFiles: () =>
                                                      _pickFiles(),
                                                  onImportDirectory:
                                                      _pickDirectory,
                                                ),
                                                _visited.contains(1)
                                                    ? IdgamesPage(
                                                        controller: controller,
                                                        onPlayEntry: (id) async {
                                                          final game =
                                                              controller.games
                                                                  .where(
                                                                    (game) =>
                                                                        rowId(
                                                                          game,
                                                                        ) ==
                                                                        id,
                                                                  )
                                                                  .firstOrNull;
                                                          if (game != null &&
                                                              mounted) {
                                                            await showPlayEntry(
                                                              context,
                                                              controller,
                                                              game,
                                                            );
                                                          }
                                                        },
                                                      )
                                                    : const SizedBox.shrink(),
                                                SourcePortsPage(
                                                  controller: controller,
                                                ),
                                                TagsPage(
                                                  controller: controller,
                                                ),
                                                SettingsPage(
                                                  controller: controller,
                                                ),
                                              ],
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ],
                                );
                              },
                            ),
                    ),
                  ],
                ),
                if (_dragging)
                  Positioned.fill(
                    child: IgnorePointer(
                      child: Container(
                        color: Theme.of(
                          context,
                        ).colorScheme.primaryContainer.withValues(alpha: 0.95),
                        child: Center(
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              const Icon(
                                Icons.file_download_outlined,
                                size: 70,
                              ),
                              const SizedBox(height: 16),
                              Text(
                                'Drop files or folders to import',
                                style: Theme.of(
                                  context,
                                ).textTheme.headlineSmall,
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
    final guardedContent = ExcludeFocus(
      excluding: controller.shutdownInProgress,
      child: AbsorbPointer(
        absorbing: controller.shutdownInProgress,
        child: content,
      ),
    );
    return defaultTargetPlatform == TargetPlatform.macOS
        ? PlatformMenuBar(
            menus: controller.shutdownInProgress
                ? const [
                    PlatformMenu(
                      label: 'Doom Launcher',
                      menus: [PlatformMenuItem(label: 'Closing…')],
                    ),
                  ]
                : _platformMenus(),
            child: guardedContent,
          )
        : guardedContent;
  }

  Future<void> _requestWindowClose() async {
    try {
      await windowManager.close();
    } catch (error) {
      widget.controller.reportError(error);
    }
  }

  List<PlatformMenuItem> _platformMenus() => [
    PlatformMenu(
      label: 'Doom Launcher',
      menus: [
        PlatformMenuItemGroup(
          members: [
            PlatformMenuItem(label: 'About Doom Launcher', onSelected: _about),
            PlatformMenuItem(
              label: 'Settings…',
              shortcut: const SingleActivator(
                LogicalKeyboardKey.comma,
                meta: true,
              ),
              onSelected: () => _navigate(4),
            ),
          ],
        ),
        const PlatformProvidedMenuItem(
          type: PlatformProvidedMenuItemType.servicesSubmenu,
        ),
        const PlatformMenuItemGroup(
          members: [
            PlatformProvidedMenuItem(type: PlatformProvidedMenuItemType.hide),
            PlatformProvidedMenuItem(
              type: PlatformProvidedMenuItemType.hideOtherApplications,
            ),
            PlatformProvidedMenuItem(
              type: PlatformProvidedMenuItemType.showAllApplications,
            ),
          ],
        ),
        PlatformMenuItem(
          label: 'Quit Doom Launcher',
          shortcut: const SingleActivator(LogicalKeyboardKey.keyQ, meta: true),
          onSelected: _requestWindowClose,
        ),
      ],
    ),
    PlatformMenu(
      label: 'File',
      menus: [
        PlatformMenuItem(
          label: 'Import files…',
          shortcut: const SingleActivator(LogicalKeyboardKey.keyO, meta: true),
          onSelected: () => _pickFiles(),
        ),
        PlatformMenuItem(
          label: 'Import base games (IWADs)…',
          onSelected: () => _pickFiles(asIwads: true),
        ),
        PlatformMenuItem(
          label: 'Import directory…',
          shortcut: const SingleActivator(
            LogicalKeyboardKey.keyO,
            meta: true,
            shift: true,
          ),
          onSelected: _pickDirectory,
        ),
        PlatformMenuItem(
          label: 'Scan installed stores…',
          onSelected: _scanStores,
        ),
        PlatformMenuItem(
          label: 'Close window',
          shortcut: const SingleActivator(LogicalKeyboardKey.keyW, meta: true),
          onSelected: _requestWindowClose,
        ),
      ],
    ),
    const PlatformMenu(
      label: 'Edit',
      menus: [
        PlatformMenuItemGroup(
          members: [
            PlatformMenuItem(
              label: 'Undo',
              shortcut: SingleActivator(LogicalKeyboardKey.keyZ, meta: true),
              onSelectedIntent: UndoTextIntent(SelectionChangedCause.keyboard),
            ),
            PlatformMenuItem(
              label: 'Redo',
              shortcut: SingleActivator(
                LogicalKeyboardKey.keyZ,
                meta: true,
                shift: true,
              ),
              onSelectedIntent: RedoTextIntent(SelectionChangedCause.keyboard),
            ),
          ],
        ),
        PlatformMenuItemGroup(
          members: [
            PlatformMenuItem(
              label: 'Cut',
              shortcut: SingleActivator(LogicalKeyboardKey.keyX, meta: true),
              onSelectedIntent: CopySelectionTextIntent.cut(
                SelectionChangedCause.keyboard,
              ),
            ),
            PlatformMenuItem(
              label: 'Copy',
              shortcut: SingleActivator(LogicalKeyboardKey.keyC, meta: true),
              onSelectedIntent: CopySelectionTextIntent.copy,
            ),
            PlatformMenuItem(
              label: 'Paste',
              shortcut: SingleActivator(LogicalKeyboardKey.keyV, meta: true),
              onSelectedIntent: PasteTextIntent(SelectionChangedCause.keyboard),
            ),
            PlatformMenuItem(
              label: 'Select All',
              shortcut: SingleActivator(LogicalKeyboardKey.keyA, meta: true),
              onSelectedIntent: SelectAllTextIntent(
                SelectionChangedCause.keyboard,
              ),
            ),
          ],
        ),
      ],
    ),
    PlatformMenu(
      label: 'Library',
      onOpen: () => setState(() {}),
      menus: [
        PlatformMenuItem(
          label: 'Play selected',
          shortcut: const SingleActivator(LogicalKeyboardKey.keyP, meta: true),
          onSelected: !_canPlaySelected
              ? null
              : () => _libraryKey.currentState?.playSelected(),
        ),
        PlatformMenuItem(
          label: 'Play with options…',
          onSelected: !_canPlaySelected
              ? null
              : () => _libraryKey.currentState?.playSelected(withOptions: true),
        ),
        PlatformMenuItem(
          label: 'Edit selected…',
          shortcut: const SingleActivator(LogicalKeyboardKey.keyE, meta: true),
          onSelected: _libraryKey.currentState?.selected == null
              ? null
              : () => _libraryKey.currentState?.editSelected(),
        ),
        PlatformMenuItem(
          label: 'Delete selected…',
          onSelected: _libraryKey.currentState?.selected == null
              ? null
              : () => _libraryKey.currentState?.deleteSelected(),
        ),
        PlatformMenuItem(
          label: 'Search library',
          shortcut: const SingleActivator(LogicalKeyboardKey.keyF, meta: true),
          onSelected: () {
            _navigate(0);
            WidgetsBinding.instance.addPostFrameCallback(
              (_) => _libraryKey.currentState?.focusSearch(),
            );
          },
        ),
      ],
    ),
    PlatformMenu(
      label: 'View',
      menus: [
        for (final entry in <int, String>{
          0: 'Library',
          if ('${widget.controller.settings['VisibleViews'] ?? 'Recent;Untagged;IWads;Id Games'}'
              .split(';')
              .contains('Id Games'))
            1: 'Get mods',
          2: 'Source ports',
          3: 'Tags',
          4: 'Settings',
        }.entries)
          PlatformMenuItem(
            label: entry.value,
            onSelected: () => _navigate(entry.key),
          ),
      ],
    ),
    const PlatformMenu(
      label: 'Window',
      menus: [
        PlatformProvidedMenuItem(
          type: PlatformProvidedMenuItemType.minimizeWindow,
        ),
        PlatformProvidedMenuItem(type: PlatformProvidedMenuItemType.zoomWindow),
        PlatformProvidedMenuItem(
          type: PlatformProvidedMenuItemType.toggleFullScreen,
        ),
        PlatformProvidedMenuItem(
          type: PlatformProvidedMenuItemType.arrangeWindowsInFront,
        ),
      ],
    ),
    PlatformMenu(
      label: 'Help',
      menus: [
        PlatformMenuItem(label: 'About Doom Launcher', onSelected: _about),
      ],
    ),
  ];

  Widget _menuBar() => MenuBar(
    children: [
      SubmenuButton(
        menuChildren: [
          MenuItemButton(
            onPressed: () => _pickFiles(),
            leadingIcon: const Icon(Icons.add),
            child: const Text('Import files…'),
          ),
          MenuItemButton(
            onPressed: () => _pickFiles(asIwads: true),
            child: const Text('Import base games (IWADs)…'),
          ),
          MenuItemButton(
            onPressed: _pickDirectory,
            child: const Text('Import directory…'),
          ),
          MenuItemButton(
            onPressed: _scanStores,
            child: const Text('Scan Steam / GOG / Heroic / Lutris…'),
          ),
          const Divider(),
          MenuItemButton(
            onPressed: _about,
            child: const Text('About Doom Launcher'),
          ),
        ],
        child: const Text('File'),
      ),
      SubmenuButton(
        onOpen: () => setState(() {}),
        menuChildren: [
          MenuItemButton(
            onPressed: !_canPlaySelected
                ? null
                : () => _libraryKey.currentState?.playSelected(),
            child: const Text('Play selected'),
          ),
          MenuItemButton(
            onPressed: !_canPlaySelected
                ? null
                : () =>
                      _libraryKey.currentState?.playSelected(withOptions: true),
            child: const Text('Play with options…'),
          ),
          MenuItemButton(
            onPressed: _libraryKey.currentState?.selected == null
                ? null
                : () => _libraryKey.currentState?.editSelected(),
            child: const Text('Edit selected…'),
          ),
          MenuItemButton(
            onPressed: _libraryKey.currentState?.selected == null
                ? null
                : () => _libraryKey.currentState?.deleteSelected(),
            child: const Text('Delete selected…'),
          ),
          const Divider(),
          MenuItemButton(
            onPressed: () => widget.controller.execute('snapshot'),
            child: const Text('Refresh library'),
          ),
        ],
        child: const Text('Library'),
      ),
      SubmenuButton(
        menuChildren: [
          for (final entry in <int, String>{
            0: 'Library',
            if ('${widget.controller.settings['VisibleViews'] ?? 'Recent;Untagged;IWads;Id Games'}'
                .split(';')
                .contains('Id Games'))
              1: 'Get mods',
            2: 'Source ports',
            3: 'Tags',
            4: 'Settings',
          }.entries)
            MenuItemButton(
              onPressed: () => _navigate(entry.key),
              child: Text(entry.value),
            ),
        ],
        child: const Text('View'),
      ),
      SubmenuButton(
        menuChildren: [
          MenuItemButton(onPressed: _about, child: const Text('About')),
          MenuItemButton(
            onPressed: () => showDialog<void>(
              context: context,
              builder: (context) => AlertDialog(
                title: const Text('Keyboard shortcuts'),
                content: const Text(
                  'Ctrl/Cmd+O  Import files\nCtrl/Cmd+Shift+O  Import directory\nCtrl/Cmd+F  Search library\nCtrl/Cmd+P  Play selected\nCtrl/Cmd+E  Edit selected\nF5  Refresh',
                ),
                actions: [
                  TextButton(
                    onPressed: () => Navigator.pop(context),
                    child: const Text('Close'),
                  ),
                ],
              ),
            ),
            child: const Text('Keyboard shortcuts'),
          ),
        ],
        child: const Text('Help'),
      ),
    ],
  );
  Widget _setupBanner() => Container(
    width: double.infinity,
    padding: const EdgeInsets.all(16),
    color: Theme.of(context).colorScheme.secondaryContainer,
    child: Wrap(
      spacing: 12,
      runSpacing: 8,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        const Icon(Icons.rocket_launch_outlined),
        const Text(
          'Set up your first game',
          style: TextStyle(fontWeight: FontWeight.bold),
        ),
        if (widget.controller.ports.isEmpty)
          OutlinedButton(
            onPressed: () => _navigate(2),
            child: const Text('1. Add a source port'),
          ),
        if (widget.controller.iwads.isEmpty)
          FilledButton.tonal(
            onPressed: () => _pickFiles(asIwads: true),
            child: const Text('2. Add base games (IWADs)'),
          ),
        TextButton(
          onPressed: _scanStores,
          child: const Text('Find installed games'),
        ),
      ],
    ),
  );
  Widget _sessionBar() => Container(
    padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
    color: Theme.of(context).colorScheme.surfaceContainer,
    child: SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        spacing: 16,
        children: widget.controller.sessions.map((session) {
          final running =
              session['status'] == 'running' || session['status'] == 'starting';
          final game = widget.controller.games
              .where((g) => rowId(g) == session['game_file_id'])
              .firstOrNull;
          return Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                running
                    ? Icons.play_circle_outline
                    : session['status'] == 'failed'
                    ? Icons.error_outline
                    : Icons.check_circle_outline,
                size: 18,
              ),
              const SizedBox(width: 8),
              ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 380),
                child: Text(
                  '${game == null ? 'Game' : gameTitle(game)} · ${session['status']} · ${((session['elapsed_seconds'] as num?)?.toInt() ?? 0) ~/ 60} min',
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              if (session['error'] != null && '${session['error']}'.isNotEmpty)
                IconButton(
                  tooltip: '${session['error']}',
                  icon: const Icon(Icons.info_outline, size: 18),
                  onPressed: () => showMessage(context, '${session['error']}'),
                ),
              if ('${session['log_path'] ?? ''}'.isNotEmpty)
                IconButton(
                  tooltip: 'Open session log',
                  icon: const Icon(Icons.receipt_long_outlined, size: 18),
                  onPressed: () =>
                      openLocalFile(context, '${session['log_path']}'),
                ),
              if (running)
                IconButton(
                  tooltip: 'Stop game',
                  icon: const Icon(Icons.stop_circle_outlined, size: 18),
                  onPressed: () async {
                    if (await confirmAction(
                      context,
                      title: 'Stop game?',
                      message:
                          'The running game will close. Unsaved progress may be lost.',
                      action: 'Stop',
                    )) {
                      await widget.controller.execute('stop_session', {
                        'id': session['id'],
                      });
                    }
                  },
                ),
            ],
          );
        }).toList(),
      ),
    ),
  );
}
