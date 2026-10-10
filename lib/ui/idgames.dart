import 'dart:async';
import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import '../state/app_controller.dart';
import 'management/helpers.dart';

/// Resolves only a file actually reported by this import, never another download.
int? importedEntryId(List<dynamic> added, List<Map<String, dynamic>> games) {
  final names = added
      .map(
        (name) => '$name'.replaceAll('\\', '/').split('/').last.toLowerCase(),
      )
      .toSet();
  final matches = games
      .where((game) => names.contains('${game['FileName']}'.toLowerCase()))
      .toList();
  return matches.length == 1 ? rowId(matches.single['GameFileID']) : null;
}

String formatDownloadBytes(num bytes) {
  if (bytes < 1024) return '${bytes.toInt()} B';
  if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(1)} KB';
  return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
}

class IdgamesPage extends StatefulWidget {
  const IdgamesPage({super.key, required this.controller, this.onPlayEntry});
  final AppController controller;
  final void Function(int)? onPlayEntry;
  @override
  State<IdgamesPage> createState() => _IdgamesPageState();
}

class _IdgamesPageState extends State<IdgamesPage> {
  final _query = TextEditingController();
  String _type = 'title';
  List<Map<String, dynamic>> _files = [];
  bool _searching = false;
  String? _searchError;
  int _searchGeneration = 0;
  int _downloadGeneration = 0;
  Map<String, dynamic>? _download;
  Map<String, dynamic>? _downloadFile;
  bool _downloadBusy = false;
  bool _polling = false;
  bool _importing = false;
  bool _playAfterImport = false;
  int? _importedId;
  bool _downloadComplete = false;
  bool _downloadDiscarded = false;
  bool _discarding = false;
  String? _downloadMessage;
  Timer? _timer;
  bool get _hasPendingImport =>
      _download?['status'] == 'completed' &&
      !_downloadComplete &&
      !_downloadDiscarded;
  bool get _canDownload =>
      !widget.controller.shutdownInProgress &&
      !_downloadBusy &&
      !_discarding &&
      !_hasPendingImport;

  Future<void> _backgroundCleanup(String operation, Object id) async {
    try {
      await widget.controller.backend.call(operation, {'id': id});
    } catch (error) {
      widget.controller.reportError('Could not clean up the download: $error');
    }
  }

  @override
  void initState() {
    super.initState();
    _search(latest: true);
  }

  @override
  void dispose() {
    _searchGeneration++;
    _downloadGeneration++;
    _timer?.cancel();
    // A page that no longer exists must not import or launch a later completion.
    final id = _download?['id'];
    if (_downloadBusy && !_importing && id != null) {
      unawaited(_backgroundCleanup('cancel_download', id));
    }
    _query.dispose();
    super.dispose();
  }

  Future<void> _search({bool latest = false}) async {
    if (!latest && _query.text.trim().isEmpty) {
      setState(
        () => _searchError =
            'Enter a title, author, filename, or description to search.',
      );
      return;
    }
    final generation = ++_searchGeneration;
    setState(() {
      _searching = true;
      _searchError = null;
    });
    try {
      final result = await widget.controller.backend.call('search_idgames', {
        'query': _query.text.trim(),
        'type': _type,
        'latest': latest,
      });
      if (!mounted || generation != _searchGeneration) return;
      setState(() {
        _files = (result['files'] as List? ?? [])
            .map((row) => Map<String, dynamic>.from(row as Map))
            .toList();
      });
    } catch (error) {
      if (mounted && generation == _searchGeneration) {
        setState(() => _searchError = 'Search failed: $error');
      }
    } finally {
      if (mounted && generation == _searchGeneration) {
        setState(() => _searching = false);
      }
    }
  }

  Future<void> _openWebsite(Map<String, dynamic> file) async {
    final uri = Uri.tryParse('${file['url'] ?? ''}');
    if (uri == null ||
        !['https', 'http'].contains(uri.scheme) ||
        uri.host.isEmpty) {
      showNotice(context, 'This file has no valid website URL.');
      return;
    }
    try {
      if (!await launchUrl(uri, mode: LaunchMode.externalApplication) &&
          mounted) {
        showNotice(context, 'Could not open the website.');
      }
    } catch (error) {
      if (mounted) showNotice(context, 'Could not open the website: $error');
    }
  }

  Future<void> _startDownload(Map<String, dynamic> file, bool play) async {
    if (!_canDownload) return;
    final generation = ++_downloadGeneration;
    _timer?.cancel();
    setState(() {
      _downloadBusy = true;
      _downloadFile = file;
      _download = null;
      _downloadMessage = null;
      _playAfterImport = play;
      _importedId = null;
      _downloadComplete = false;
      _downloadDiscarded = false;
    });
    try {
      final result = await widget.controller.execute('start_download', {
        'directory': file['directory'] ?? '',
        'file_name': file['file_name'],
      });
      if (result == null) {
        throw StateError(
          widget.controller.error ?? 'The download could not start.',
        );
      }
      final job = Map<String, dynamic>.from(result['download'] as Map);
      if (!mounted || generation != _downloadGeneration) {
        // Dispose can happen while the worker is allocating the job.
        unawaited(_backgroundCleanup('cancel_download', job['id']));
        return;
      }
      setState(() => _download = job);
      await _handleStatus(job, generation);
      if (!mounted ||
          generation != _downloadGeneration ||
          !_downloadBusy ||
          _importing) {
        return;
      }
      _timer = Timer.periodic(
        const Duration(milliseconds: 500),
        (_) => _poll(generation),
      );
    } catch (error) {
      if (mounted && generation == _downloadGeneration) {
        setState(() {
          _downloadBusy = false;
          _downloadMessage = 'Download failed: $error';
        });
      }
    }
  }

  Future<void> _poll(int generation) async {
    if (_polling ||
        !_downloadBusy ||
        _importing ||
        generation != _downloadGeneration ||
        _download == null) {
      return;
    }
    _polling = true;
    try {
      final result = await widget.controller.backend.call('download_status', {
        'id': _download!['id'],
      });
      if (!mounted || generation != _downloadGeneration) return;
      final job = Map<String, dynamic>.from(result['download'] as Map);
      setState(() => _download = job);
      await _handleStatus(job, generation);
    } catch (error) {
      _timer?.cancel();
      if (mounted && generation == _downloadGeneration) {
        setState(() {
          _downloadBusy = false;
          _downloadMessage = 'Could not check the download: $error';
        });
      }
    } finally {
      _polling = false;
    }
  }

  Future<void> _handleStatus(Map<String, dynamic> job, int generation) async {
    if (!mounted || generation != _downloadGeneration) return;
    final status = '${job['status']}';
    if (status == 'completed') {
      _timer?.cancel();
      if (_importing || _downloadComplete) return;
      final path = '${job['local_path'] ?? ''}';
      if (path.isEmpty) {
        setState(() {
          _downloadBusy = false;
          _downloadMessage =
              'The completed download did not provide a file path.';
        });
        return;
      }
      setState(() {
        _downloadBusy = true;
        _importing = true;
        _downloadMessage = 'Importing into your library…';
      });
      final result = await widget.controller.execute('import_files', {
        'paths': [path],
        'as_iwads': false,
      });
      if (!mounted || generation != _downloadGeneration) {
        if (result != null &&
            (result['added'] as List? ?? []).isNotEmpty &&
            (result['failed'] as List? ?? []).isEmpty) {
          unawaited(_backgroundCleanup('discard_download', job['id']));
        }
        return;
      }
      if (result == null) {
        setState(() {
          _downloadBusy = false;
          _importing = false;
          _downloadMessage =
              'Import failed: ${widget.controller.error ?? 'unknown error'}. Retry importing or discard this download before starting another.';
        });
        return;
      }
      final added = result['added'] as List? ?? [];
      final failed = result['failed'] as List? ?? [];
      final id = importedEntryId(added, widget.controller.games);
      setState(() {
        _downloadComplete = added.isNotEmpty && failed.isEmpty;
        _importedId = id;
        _downloadMessage = added.isEmpty
            ? 'No files were imported.'
            : 'Imported ${added.length} file${added.length == 1 ? '' : 's'} into your library.';
        if (failed.isNotEmpty) {
          _downloadMessage =
              '$_downloadMessage ${failed.map((item) => item['message']).join('; ')}';
        }
        if (_playAfterImport && id == null && added.isNotEmpty) {
          _downloadMessage =
              '$_downloadMessage Choose the imported files in the library to play.';
        }
      });
      if (_downloadComplete) {
        await _discardDownloadedFile();
      }
      if (_hasPendingImport) {
        _downloadMessage =
            '$_downloadMessage Retry importing or discard this download before starting another.';
      }
      if (!mounted || generation != _downloadGeneration) return;
      setState(() {
        _downloadBusy = false;
        _importing = false;
      });
      if (_playAfterImport &&
          id != null &&
          !widget.controller.shutdownInProgress) {
        widget.onPlayEntry?.call(id);
      }
    } else if (status == 'failed' || status == 'cancelled') {
      _timer?.cancel();
      setState(() {
        _downloadBusy = false;
        _downloadMessage = status == 'cancelled'
            ? 'Download cancelled.'
            : 'Download failed: ${job['error'] ?? 'unknown error'}';
      });
    }
  }

  Future<void> _discardDownloadedFile({bool dismiss = false}) async {
    final id = _download?['id'];
    if (id == null || _discarding) return;
    setState(() => _discarding = true);
    try {
      await widget.controller.backend.call('discard_download', {'id': id});
      if (!mounted) return;
      setState(() {
        _downloadDiscarded = true;
        if (dismiss) _downloadFile = null;
      });
    } catch (error) {
      if (mounted) {
        setState(
          () => _downloadMessage =
              '${_downloadMessage ?? ''} Could not remove the temporary download: $error',
        );
      }
    } finally {
      if (mounted) setState(() => _discarding = false);
    }
  }

  Future<void> _retry() async {
    if (_downloadBusy || _discarding) return;
    final status = _download?['status'];
    if (status == 'completed') {
      await _handleStatus(_download!, _downloadGeneration);
    } else if (status == 'pending' || status == 'downloading') {
      setState(() {
        _downloadBusy = true;
        _downloadMessage = null;
      });
      await _poll(_downloadGeneration);
      if (mounted && _downloadBusy && !_importing) {
        _timer = Timer.periodic(
          const Duration(milliseconds: 500),
          (_) => _poll(_downloadGeneration),
        );
      }
    } else {
      await _startDownload(_downloadFile!, _playAfterImport);
    }
  }

  Future<void> _cancel() async {
    final id = _download?['id'];
    if (id == null || _importing) return;
    final generation = ++_downloadGeneration;
    _timer?.cancel();
    try {
      final result = await widget.controller.backend.call('cancel_download', {
        'id': id,
      });
      if (mounted && generation == _downloadGeneration) {
        setState(() {
          _download = result['download'] is Map
              ? Map<String, dynamic>.from(result['download'] as Map)
              : {...?_download, 'status': 'cancelled'};
          _downloadBusy = false;
          _playAfterImport = false;
          _downloadMessage = _download?['status'] == 'completed'
              ? 'The download finished before cancellation. Import or discard it.'
              : 'Download cancelled.';
        });
      }
    } catch (error) {
      if (mounted && generation == _downloadGeneration) {
        setState(() => _downloadMessage = 'Could not cancel: $error');
        _timer = Timer.periodic(
          const Duration(milliseconds: 500),
          (_) => _poll(generation),
        );
      }
    }
  }

  Widget _downloadCard() {
    final received = (_download?['received'] as num?) ?? 0;
    final total = (_download?['total'] as num?) ?? 0;
    return Card(
      margin: const EdgeInsets.fromLTRB(24, 0, 24, 16),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${_downloadFile?['file_name'] ?? 'Download'}',
              style: Theme.of(context).textTheme.titleSmall,
            ),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                if (_downloadBusy && !_importing)
                  TextButton.icon(
                    onPressed: _download == null ? null : _cancel,
                    icon: const Icon(Icons.close),
                    label: const Text('Cancel'),
                  ),
                if (!_downloadBusy &&
                    _importedId != null &&
                    widget.onPlayEntry != null)
                  TextButton.icon(
                    onPressed: () => widget.onPlayEntry!(_importedId!),
                    icon: const Icon(Icons.play_arrow),
                    label: const Text('Play'),
                  ),
                if (!_downloadBusy && !_downloadComplete)
                  TextButton.icon(
                    onPressed: _discarding ? null : _retry,
                    icon: const Icon(Icons.refresh),
                    label: Text(
                      _download?['status'] == 'completed'
                          ? 'Retry import'
                          : 'Retry',
                    ),
                  ),
                if (!_downloadBusy &&
                    !_downloadDiscarded &&
                    _download?['status'] == 'completed')
                  TextButton.icon(
                    onPressed: _discarding
                        ? null
                        : () => _discardDownloadedFile(dismiss: true),
                    icon: const Icon(Icons.delete_outline),
                    label: Text(
                      _discarding ? 'Discarding…' : 'Discard download',
                    ),
                  ),
              ],
            ),
            if (_downloadBusy) ...[
              const SizedBox(height: 8),
              LinearProgressIndicator(
                value: !_importing && total > 0
                    ? (received / total).clamp(0.0, 1.0)
                    : null,
              ),
              const SizedBox(height: 8),
              Text(
                _importing
                    ? 'Importing…'
                    : '${formatDownloadBytes(received)}${total > 0 ? ' / ${formatDownloadBytes(total)}' : ''}',
              ),
            ],
            if (_downloadMessage != null) Text(_downloadMessage!),
          ],
        ),
      ),
    );
  }

  Future<void> _showDetails(Map<String, dynamic> file) => showDialog<void>(
    context: context,
    builder: (dialogContext) => AlertDialog(
      title: Text(_title(file)),
      content: SizedBox(
        width: 620,
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              SelectableText('${file['file_name'] ?? ''}'),
              const SizedBox(height: 12),
              Text('Author: ${file['author'] ?? 'Unknown'}'),
              Text('Released: ${file['release_date'] ?? 'Unknown'}'),
              Text('Rating: ${file['rating'] ?? 'Unrated'}'),
              if (file['size'] is num)
                Text('Size: ${formatDownloadBytes(file['size'] as num)}'),
              const SizedBox(height: 16),
              SelectableText(
                '${file['description'] ?? 'No description provided.'}',
              ),
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(dialogContext),
          child: const Text('Close'),
        ),
        if ('${file['url'] ?? ''}'.isNotEmpty)
          TextButton.icon(
            onPressed: () => _openWebsite(file),
            icon: const Icon(Icons.open_in_new),
            label: const Text('Website'),
          ),
        FilledButton.icon(
          onPressed: !_canDownload
              ? null
              : () {
                  Navigator.pop(dialogContext);
                  _startDownload(file, false);
                },
          icon: const Icon(Icons.download),
          label: const Text('Download'),
        ),
      ],
    ),
  );
  String _title(Map<String, dynamic> file) =>
      '${file['title'] ?? ''}'.trim().isEmpty
      ? '${file['file_name'] ?? 'Untitled'}'
      : '${file['title']}';
  Widget _searchControls() => Padding(
    padding: const EdgeInsets.fromLTRB(24, 0, 24, 16),
    child: LayoutBuilder(
      builder: (context, constraints) {
        final type = DropdownButtonFormField<String>(
          isExpanded: true,
          initialValue: _type,
          decoration: const InputDecoration(labelText: 'Search by'),
          items: const [
            DropdownMenuItem(value: 'title', child: Text('Title')),
            DropdownMenuItem(value: 'author', child: Text('Author')),
            DropdownMenuItem(value: 'name', child: Text('Filename')),
            DropdownMenuItem(value: 'description', child: Text('Description')),
          ],
          onChanged: (value) => setState(() => _type = value!),
        );
        final query = TextField(
          controller: _query,
          decoration: const InputDecoration(
            labelText: 'Search idgames',
            prefixIcon: Icon(Icons.search),
          ),
          onSubmitted: (_) => _search(),
        );
        final actions = Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            FilledButton(
              onPressed: () => _search(),
              child: const Text('Search'),
            ),
            OutlinedButton(
              onPressed: () => _search(latest: true),
              child: const Text('Latest'),
            ),
          ],
        );
        final largeText = MediaQuery.textScalerOf(context).scale(16) > 24;
        if (constraints.maxWidth < 430 || largeText) {
          return Column(
            children: [
              type,
              const SizedBox(height: 12),
              query,
              const SizedBox(height: 12),
              Align(alignment: Alignment.centerRight, child: actions),
            ],
          );
        }
        final fields = Row(
          children: [
            SizedBox(width: 180, child: type),
            const SizedBox(width: 12),
            Expanded(child: query),
          ],
        );
        if (constraints.maxWidth < 650) {
          return Column(
            children: [
              fields,
              const SizedBox(height: 12),
              Align(alignment: Alignment.centerRight, child: actions),
            ],
          );
        }
        return Row(
          children: [
            Expanded(child: fields),
            const SizedBox(width: 12),
            actions,
          ],
        );
      },
    ),
  );

  Widget _resultTile(Map<String, dynamic> file) => Card(
    child: ListTile(
      onTap: () => _showDetails(file),
      title: Text(_title(file), maxLines: 2, overflow: TextOverflow.ellipsis),
      subtitle: Text(
        [
          '${file['file_name'] ?? ''}',
          '${file['author'] ?? ''}',
          '${file['release_date'] ?? ''}',
        ].where((value) => value.isNotEmpty).join(' · '),
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
      ),
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          IconButton(
            tooltip: 'Download and import',
            onPressed: _canDownload ? () => _startDownload(file, false) : null,
            icon: const Icon(Icons.download),
          ),
          if (widget.onPlayEntry != null)
            IconButton(
              tooltip: 'Download and play',
              onPressed: _canDownload ? () => _startDownload(file, true) : null,
              icon: const Icon(Icons.play_arrow),
            ),
          if ('${file['url'] ?? ''}'.isNotEmpty)
            IconButton(
              tooltip: 'View website',
              onPressed: () => _openWebsite(file),
              icon: const Icon(Icons.open_in_new),
            ),
        ],
      ),
    ),
  );

  @override
  Widget build(BuildContext context) => CustomScrollView(
    slivers: [
      const SliverToBoxAdapter(
        child: PageIntro(
          'Get mods',
          'Browse the idgames archive. Downloads are imported into your library.',
        ),
      ),
      SliverToBoxAdapter(child: _searchControls()),
      if (_downloadFile != null) SliverToBoxAdapter(child: _downloadCard()),
      if (_searching)
        const SliverToBoxAdapter(child: LinearProgressIndicator()),
      if (_searchError != null)
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: SelectableText(
              _searchError!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ),
        ),
      if (_files.isEmpty)
        SliverFillRemaining(
          hasScrollBody: false,
          child: Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Text(
                _searching
                    ? 'Searching the archive…'
                    : _searchError == null
                    ? 'No matches. Try a different search or browse the latest uploads.'
                    : 'Check the download settings and try again.',
              ),
            ),
          ),
        )
      else
        SliverPadding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          sliver: SliverList.builder(
            itemCount: _files.length,
            itemBuilder: (context, index) => _resultTile(_files[index]),
          ),
        ),
    ],
  );
}
