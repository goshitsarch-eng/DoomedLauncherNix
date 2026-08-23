using DoomLauncher.Config;
using DoomLauncher.DataSources;
using DoomLauncher.GameStores;
using DoomLauncher.Handlers;
using DoomLauncher.Handlers.Sync;
using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DoomLauncher.Linux
{
    public class MainWindow
    {
        public Gtk.ApplicationWindow Native { get; }
        public void Present() => Native.Present();
        public static implicit operator Gtk.Window(MainWindow w) => w.Native;

        private readonly Adw.Application m_app;
        private readonly LaunchArgs m_launchArgs;
        private readonly LibraryOperations m_ops;
        private readonly Adw.ToastOverlay m_toasts;
        private readonly Gtk.SearchEntry m_search;
        private readonly Gtk.CheckButton m_includeAll;
        private readonly Gtk.Notebook m_tabs;
        private readonly Gtk.ListBox m_list;
        private readonly Gtk.FlowBox m_tiles;
        private readonly Gtk.Stack m_viewStack;
        private readonly Gtk.Label m_summaryTitle;
        private readonly Gtk.Label m_summaryMeta;
        private readonly Gtk.Label m_summaryDesc;
        private readonly Gtk.Picture m_summaryImage;
        private readonly Gtk.ListBox m_screenshots;
        private readonly Gtk.ListBox m_saves;
        private readonly Gtk.ListBox m_demos;
        private readonly Gtk.Label m_status;
        private readonly Gtk.Button m_updateButton;
        private readonly Gtk.Button m_resyncButton;
        private readonly Gtk.ProgressBar m_progress;
        private readonly Gtk.Popover m_contextMenu;
        private readonly Gtk.Box m_contextBox;

        private List<LibraryTab> m_tabDefs = new List<LibraryTab>();
        private List<IGameFile> m_currentFiles = new List<IGameFile>();
        private readonly List<IGameFile> m_selected = new List<IGameFile>();
        private IdGamesDataAdapater m_idGames;
        private DownloadHandler m_downloadHandler;
        private readonly DownloadsWindow m_downloads;
        private ApplicationUpdateInfo m_updateInfo;
        private bool m_closingAfterPlay;
        private readonly Dictionary<int, IFileData> m_associationByRow = new Dictionary<int, IFileData>();
        private GameFileFieldType m_sortField = GameFileFieldType.Title;
        private bool m_sortDesc;
        private bool m_playAfterDownload;
        private string m_playAfterDownloadName;

        public MainWindow(Adw.Application app, LaunchArgs launchArgs)
        {
            m_app = app;
            m_launchArgs = launchArgs;
            Native = Gtk.ApplicationWindow.New(app);
            Native.SetTitle("Doom Launcher");
            m_ops = new LibraryOperations();
            m_ops.SyncProgress += (file, current, total) => GtkUtil.RunOnUi(() =>
            {
                m_progress.Visible = true;
                m_progress.Fraction = total == 0 ? 0 : (double)current / total;
                m_status.SetLabel($"Syncing {Path.GetFileName(file)} ({current}/{total})");
            });
            m_ops.ProcessExited += info => GtkUtil.RunOnUi(() =>
            {
                m_ops.HandleProcessExited(info);
                if (m_launchArgs.AutoClose && m_closingAfterPlay)
                    Native.Close();
                ReloadCurrentTab();
            });

            Native.SetTitle("Doom Launcher");
            var cfg = DataCache.Instance.AppConfiguration;
            Native.SetDefaultSize(Math.Max(900, cfg.AppWidth), Math.Max(600, cfg.AppHeight));

            m_idGames = new IdGamesDataAdapater(cfg.IdGamesUrl, cfg.ApiPage, cfg.MirrorUrl);
            m_downloads = new DownloadsWindow(this);
            m_downloadHandler = new DownloadHandler(cfg.TempDirectory, m_downloads);
            m_downloads.UserPlay += (s, e) => HandlePlay(PlayForceDialog: false);
            m_downloadHandler.ItemDownloadCompleted += OnItemDownloadCompleted;

            var header = Adw.HeaderBar.New();
            var menuButton = Gtk.MenuButton.New();
            menuButton.SetIconName("open-menu-symbolic");
            menuButton.SetMenuModel(BuildMainMenu());
            header.PackStart(menuButton);

            m_search = Gtk.SearchEntry.New();
            m_search.SetPlaceholderText("Search title, author, filename...");
            m_search.Hexpand = true;
            m_search.OnSearchChanged += (s, e) => ReloadCurrentTab();
            m_search.OnActivate += (s, e) => ReloadCurrentTab();
            header.SetTitleWidget(m_search);

            m_includeAll = Gtk.CheckButton.NewWithLabel("Include all");
            header.PackEnd(m_includeAll);

            var play = Gtk.Button.NewWithMnemonic("_Play");
            play.AddCssClass("suggested-action");
            play.OnClicked += (s, e) => HandlePlay(false);
            header.PackEnd(play);

            var mods = Gtk.Button.NewFromIconName("system-software-install-symbolic");
            mods.SetTooltipText("Get mods");
            mods.OnClicked += (s, e) => GetModsDialog.Show(this, m_idGames, QueueDownload);
            header.PackEnd(mods);

            var setup = Gtk.Button.NewFromIconName("emblem-system-symbolic");
            setup.SetTooltipText("Setup assistant");
            setup.OnClicked += (s, e) => OpenSetupWizard();
            header.PackEnd(setup);

            var downloads = Gtk.Button.NewFromIconName("folder-download-symbolic");
            downloads.SetTooltipText("Downloads");
            downloads.OnClicked += (s, e) => m_downloads.Present();
            header.PackEnd(downloads);

            var tags = Gtk.Button.NewFromIconName("tag-symbolic");
            tags.SetTooltipText("Tags");
            tags.OnClicked += (s, e) => TagsDialog.Show(this, () => { RebuildTabs(); ReloadCurrentTab(); });
            header.PackEnd(tags);

            m_updateButton = Gtk.Button.NewFromIconName("software-update-available-symbolic");
            m_updateButton.SetTooltipText("Update available");
            m_updateButton.Visible = false;
            m_updateButton.AddCssClass("suggested-action");
            m_updateButton.OnClicked += (s, e) => ShowUpdate();
            header.PackEnd(m_updateButton);

            m_resyncButton = Gtk.Button.NewFromIconName("view-refresh-symbolic");
            m_resyncButton.SetTooltipText("Resync recommended");
            m_resyncButton.Visible = false;
            m_resyncButton.OnClicked += (s, e) => HandleResyncRecommended();
            header.PackEnd(m_resyncButton);

            var viewToggle = Gtk.Button.NewFromIconName("view-grid-symbolic");
            viewToggle.SetTooltipText("Toggle list / tile view");
            viewToggle.OnClicked += (s, e) => ToggleView();
            header.PackEnd(viewToggle);

            m_tabs = Gtk.Notebook.New();
            m_tabs.OnSwitchPage += (s, e) => ReloadCurrentTab();

            m_list = Gtk.ListBox.New();
            m_list.SetSelectionMode(Gtk.SelectionMode.Multiple);
            m_list.AddCssClass("boxed-list");
            m_list.OnRowActivated += (s, e) => HandlePlay(false);
            m_list.OnSelectedRowsChanged += (s, e) => OnSelectionChanged();

            m_tiles = Gtk.FlowBox.New();
            m_tiles.SetSelectionMode(Gtk.SelectionMode.Multiple);
            m_tiles.SetMaxChildrenPerLine(6);
            m_tiles.SetMinChildrenPerLine(2);
            m_tiles.SetHomogeneous(true);
            m_tiles.OnChildActivated += (s, e) => HandlePlay(false);
            m_tiles.OnSelectedChildrenChanged += (s, e) => OnTileSelectionChanged();

            m_viewStack = Gtk.Stack.New();
            m_viewStack.AddTitled(GtkUtil.Scroll(m_list), "list", "List");
            m_viewStack.AddTitled(GtkUtil.Scroll(m_tiles), "tiles", "Tiles");
            ApplyViewType(cfg.GameFileViewType);

            m_summaryTitle = Gtk.Label.New("Select a file");
            m_summaryTitle.AddCssClass("title-2");
            m_summaryTitle.SetXalign(0);
            m_summaryTitle.SetWrap(true);
            m_summaryMeta = Gtk.Label.New(string.Empty);
            m_summaryMeta.SetXalign(0);
            m_summaryMeta.AddCssClass("dim-label");
            m_summaryMeta.SetWrap(true);
            m_summaryDesc = Gtk.Label.New(string.Empty);
            m_summaryDesc.SetXalign(0);
            m_summaryDesc.SetWrap(true);
            m_summaryDesc.SetYalign(0);
            m_summaryImage = Gtk.Picture.New();
            m_summaryImage.SetContentFit(Gtk.ContentFit.Contain);
            m_summaryImage.SetSizeRequest(280, 200);

            var summaryBox = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            summaryBox.SetMarginStart(8);
            summaryBox.SetMarginEnd(8);
            summaryBox.SetMarginTop(8);
            summaryBox.Append(m_summaryImage);
            summaryBox.Append(m_summaryTitle);
            summaryBox.Append(m_summaryMeta);
            summaryBox.Append(m_summaryDesc);

            var assoc = Gtk.Notebook.New();
            m_screenshots = AssociationList();
            m_saves = AssociationList();
            m_demos = AssociationList();
            assoc.AppendPage(GtkUtil.Scroll(m_screenshots), Gtk.Label.New("Screenshots"));
            assoc.AppendPage(GtkUtil.Scroll(m_saves), Gtk.Label.New("Saves"));
            assoc.AppendPage(GtkUtil.Scroll(m_demos), Gtk.Label.New("Demos"));

            var assocButtons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
            assocButtons.Append(GtkUtil.Button("Import", ImportCurrentAssociation));
            assocButtons.Append(GtkUtil.Button("Open", OpenCurrentAssociation));
            assocButtons.Append(GtkUtil.Button("Edit", EditCurrentAssociation));
            assocButtons.Append(GtkUtil.Button("Delete", DeleteCurrentAssociation));
            assocButtons.Append(GtkUtil.Button("Set Main", SetMainScreenshot));

            var right = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            right.Append(GtkUtil.Scroll(summaryBox));
            right.Append(assoc);
            right.Append(assocButtons);
            right.SetSizeRequest(320, 200);

            var split = Gtk.Paned.New(Gtk.Orientation.Horizontal);
            var left = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
            left.Append(m_tabs);
            left.Vexpand = true;
            m_viewStack.Vexpand = true;
            left.Append(m_viewStack);
            split.SetStartChild(left);
            split.SetEndChild(right);
            split.SetResizeStartChild(true);
            split.SetResizeEndChild(false);
            split.SetPosition((int)Math.Max(400, cfg.SplitLeftRight));

            m_status = Gtk.Label.New("Ready");
            m_status.SetXalign(0);
            m_status.Hexpand = true;
            m_progress = Gtk.ProgressBar.New();
            m_progress.Visible = false;
            m_progress.SetSizeRequest(180, 8);
            var statusBar = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            statusBar.SetMarginStart(8);
            statusBar.SetMarginEnd(8);
            statusBar.SetMarginBottom(4);
            statusBar.Append(m_status);
            statusBar.Append(m_progress);

            var content = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
            var toolbarView = Adw.ToolbarView.New();
            toolbarView.AddTopBar(header);
            toolbarView.SetContent(split);
            content.Append(toolbarView);
            content.Append(statusBar);

            m_toasts = Adw.ToastOverlay.New();
            m_toasts.SetChild(content);
            Native.SetChild(m_toasts);

            m_contextBox = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
            m_contextMenu = Gtk.Popover.New();
            m_contextMenu.SetChild(m_contextBox);
            m_contextMenu.SetParent(m_list);
            BuildContextMenu();

            var click = Gtk.GestureClick.New();
            click.SetButton(3);
            click.OnPressed += (s, e) => m_contextMenu.Popup();
            m_list.AddController(click);
            var clickTiles = Gtk.GestureClick.New();
            clickTiles.SetButton(3);
            clickTiles.OnPressed += (s, e) => m_contextMenu.Popup();
            m_tiles.AddController(clickTiles);

            AddDropTarget(m_list);
            AddDropTarget(m_tiles);

            AddActions();
            RebuildTabs();
            OnShown();
        }

        private Gtk.ListBox AssociationList()
        {
            var list = Gtk.ListBox.New();
            list.SetSelectionMode(Gtk.SelectionMode.Single);
            list.AddCssClass("boxed-list");
            list.OnRowActivated += (s, e) => OpenCurrentAssociation();
            return list;
        }

        private void AddDropTarget(Gtk.Widget widget)
        {
            // File drag-and-drop is handled via Add Files; GTK 4 DropTarget value boxing varies by GirCore version.
        }

        private Gio.Menu BuildMainMenu()
        {
            var menu = Gio.Menu.New();
            var add = Gio.Menu.New();
            add.Append("Add Files...", "win.add-files");
            add.Append("Add Directory...", "win.add-directory");
            add.Append("Add IWADs...", "win.add-iwads");
            add.Append("Add Files Recursively...", "win.add-recursive");
            add.Append("Load WADs from Steam/GOG...", "win.load-stores");
            add.Append("Setup assistant...", "win.setup-wizard");
            add.Append("Get mods...", "win.get-mods");
            menu.AppendSection(null, add);

            var ports = Gio.Menu.New();
            ports.Append("Source Ports...", "win.source-ports");
            ports.Append("Utilities...", "win.utilities");
            ports.Append("Doom 64...", "win.doom64");
            ports.Append("Create Zip...", "win.create-zip");
            menu.AppendSection(null, ports);

            var config = Gio.Menu.New();
            config.Append("Settings...", "win.settings");
            config.Append("Manage Tags...", "win.tags");
            menu.AppendSection(null, config);

            var play = Gio.Menu.New();
            play.Append("Play Now", "win.play");
            play.Append("Play Random...", "win.play-random");
            menu.AppendSection(null, play);

            var tools = Gio.Menu.New();
            tools.Append("Generate Text File...", "win.generate-txt");
            tools.Append("Cumulative Statistics...", "win.stats");
            tools.Append("Global Cumulative Statistics...", "win.stats-global");
            menu.AppendSection(null, tools);

            var help = Gio.Menu.New();
            help.Append("About", "win.about");
            help.Append("Help", "win.help");
            help.Append("Manual Update...", "win.manual-update");
            menu.AppendSection(null, help);
            return menu;
        }

        private void AddActions()
        {
            AddAction("add-files", () => GtkUtil.OpenFiles(this, "Select Game Files",
                new[] { "*.zip", "*.wad", "*.pk3", "*.pk7", "*.rar", "*.7z", "*.txt", "*.zdl" },
                files => AddFiles(files, AddFileType.GameFile)));
            AddAction("add-directory", () => GtkUtil.OpenFolder(this, "Select Folder", folder =>
            {
                if (string.IsNullOrEmpty(folder))
                    return;
                var files = Directory.GetFiles(folder);
                AddFiles(files, AddFileType.GameFile);
            }));
            AddAction("add-iwads", () => GtkUtil.OpenFiles(this, "Select IWADs",
                new[] { "*.wad", "*.iwad", "*.ipk3" },
                files => AddFiles(files, AddFileType.IWad)));
            AddAction("add-recursive", () => GtkUtil.OpenFolder(this, "Select Folder", AddRecursive));
            AddAction("load-stores", LoadStores);
            AddAction("setup-wizard", OpenSetupWizard);
            AddAction("get-mods", () => GetModsDialog.Show(this, m_idGames, QueueDownload));
            AddAction("source-ports", () => SourcePortsDialog.Show(this, SourcePortLaunchType.SourcePort, ReloadCurrentTab));
            AddAction("utilities", () => SourcePortsDialog.Show(this, SourcePortLaunchType.Utility, ReloadCurrentTab));
            AddAction("doom64", () => SourcePortsDialog.Show(this, SourcePortLaunchType.Doom64, ReloadCurrentTab));
            AddAction("create-zip", CreateZip);
            AddAction("settings", () => SettingsDialog.Show(this, () =>
            {
                GtkUtil.ApplyColorTheme(DataCache.Instance.AppConfiguration.ColorTheme);
                RebuildTabs();
                ApplyViewType(DataCache.Instance.AppConfiguration.GameFileViewType);
                ReloadCurrentTab();
            }));
            AddAction("tags", () => TagsDialog.Show(this, () => { RebuildTabs(); ReloadCurrentTab(); }));
            AddAction("play", () => HandlePlay(false));
            AddAction("play-random", PlayRandom);
            AddAction("generate-txt", () => TxtGeneratorDialog.Show(this, SelectedFile()));
            AddAction("stats", () => StatsDialog.Show(this, CurrentTabTitle(), m_currentFiles, false));
            AddAction("stats-global", () =>
            {
                var files = DataCache.Instance.DataSourceAdapter.GetGameFiles().ToList();
                StatsDialog.Show(this, "Global", files, true);
            });
            AddAction("about", ShowAbout);
            AddAction("help", OpenHelp);
            AddAction("manual-update", ManualUpdate);
        }

        private void AddAction(string name, Action action)
        {
            var simple = Gio.SimpleAction.New(name, null);
            simple.OnActivate += (s, e) => action();
            Native.AddAction(simple);
        }

        private void BuildContextMenu()
        {
            void item(string label, Action action)
            {
                var btn = Gtk.Button.NewWithLabel(label);
                btn.SetHasFrame(false);
                btn.OnClicked += (s, e) =>
                {
                    m_contextMenu.Popdown();
                    action();
                };
                m_contextBox.Append(btn);
            }

            item("View Text File...", ViewText);
            item("Open Archive...", OpenArchive);
            item("Play...", () => HandlePlay(true));
            item("Edit...", EditSelected);
            item("Resync", () => ResyncSelected(true));
            item("Resync (Ignore Titlepic)", () => ResyncSelected(false));
            item("Update metadata...", UpdateMetadata);
            item("Sort by Title", () => SetSort(GameFileFieldType.Title));
            item("Sort by Author", () => SetSort(GameFileFieldType.Author));
            item("Sort by Filename", () => SetSort(GameFileFieldType.Filename));
            item("Sort by Rating", () => SetSort(GameFileFieldType.Rating));
            item("Sort by Last Played", () => SetSort(GameFileFieldType.LastPlayed));
            item("Sort by Release Date", () => SetSort(GameFileFieldType.ReleaseDate));
            item("Manage Tags...", () => TagsDialog.Show(this, () => { RebuildTabs(); ReloadCurrentTab(); }));
            item("Tag selected...", TagSelected);
            item("Utility...", RunUtility);
            item("Delete...", DeleteSelected);
            item("Rename...", RenameSelected);
            item("Generate Text File...", () => TxtGeneratorDialog.Show(this, SelectedFile()));
            item("Cumulative Statistics...", () => StatsDialog.Show(this, CurrentTabTitle(), SelectedFiles().DefaultIfEmpty().Where(x => x != null).ToList(), false));
            item("Create Shortcut", () => CreateShortcut(false));
            item("Create Auto-Play Shortcut", () => CreateShortcut(true));
            item("Download...", DownloadSelected);
            item("View Web Page...", ViewWebPage);
        }

        private void RebuildTabs()
        {
            int count = m_tabs.GetNPages();
            for (int i = count - 1; i >= 0; i--)
                m_tabs.RemovePage(i);
            m_tabDefs = LibraryTabService.BuildTabs(DataCache.Instance.DataSourceAdapter, DataCache.Instance.AppConfiguration);
            foreach (var tab in m_tabDefs)
                m_tabs.AppendPage(Gtk.Label.New(""), Gtk.Label.New(tab.Title));
            m_tabs.SetShowTabs(DataCache.Instance.AppConfiguration.ShowTabHeaders);
            int last = DataCache.Instance.AppConfiguration.LastSelectedTabIndex;
            if (last >= 0 && last < m_tabDefs.Count)
                m_tabs.SetCurrentPage(last);
        }

        private LibraryTab CurrentTab()
        {
            int i = m_tabs.GetCurrentPage();
            if (i < 0 || i >= m_tabDefs.Count)
                return m_tabDefs.FirstOrDefault();
            return m_tabDefs[i];
        }

        private string CurrentTabTitle() => CurrentTab()?.Title ?? "Library";

        private void ReloadCurrentTab()
        {
            var tab = CurrentTab();
            if (tab == null)
                return;
            string text = m_search.GetText();
            bool includeAll = m_includeAll.GetActive();
            m_currentFiles = SortFiles(m_ops.Search(tab, text, includeAll, tab.Kind == LibraryTabKind.IdGames ? m_idGames : null).ToList());
            RenderFiles();
            m_status.SetLabel($"{m_currentFiles.Count} files");
            m_progress.Visible = false;
        }

        private void SetSort(GameFileFieldType field)
        {
            if (m_sortField == field)
                m_sortDesc = !m_sortDesc;
            else
            {
                m_sortField = field;
                m_sortDesc = field == GameFileFieldType.LastPlayed || field == GameFileFieldType.Downloaded || field == GameFileFieldType.Rating;
            }
            ReloadCurrentTab();
        }

        private List<IGameFile> SortFiles(List<IGameFile> files)
        {
            IOrderedEnumerable<IGameFile> ordered = m_sortField switch
            {
                GameFileFieldType.Author => m_sortDesc ? files.OrderByDescending(f => f.Author) : files.OrderBy(f => f.Author),
                GameFileFieldType.Filename => m_sortDesc ? files.OrderByDescending(f => f.FileNameNoPath) : files.OrderBy(f => f.FileNameNoPath),
                GameFileFieldType.ReleaseDate => m_sortDesc ? files.OrderByDescending(f => f.ReleaseDate) : files.OrderBy(f => f.ReleaseDate),
                GameFileFieldType.LastPlayed => m_sortDesc ? files.OrderByDescending(f => f.LastPlayed) : files.OrderBy(f => f.LastPlayed),
                GameFileFieldType.Rating => m_sortDesc ? files.OrderByDescending(f => f.Rating) : files.OrderBy(f => f.Rating),
                GameFileFieldType.Downloaded => m_sortDesc ? files.OrderByDescending(f => f.Downloaded) : files.OrderBy(f => f.Downloaded),
                _ => m_sortDesc ? files.OrderByDescending(f => f.Title) : files.OrderBy(f => f.Title)
            };
            return ordered.ToList();
        }

        private void RenderFiles()
        {
            GtkUtil.ClearList(m_list);
            GtkUtil.ClearFlow(m_tiles);

            foreach (var file in m_currentFiles)
            {
                var row = Gtk.ListBoxRow.New();
                row.Name = file.GameFileID?.ToString() ?? file.FileName;
                var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 12);
                box.SetMarginStart(8);
                box.SetMarginEnd(8);
                box.SetMarginTop(6);
                box.SetMarginBottom(6);
                var left = Gtk.Box.New(Gtk.Orientation.Vertical, 2);
                var title = Gtk.Label.New(string.IsNullOrEmpty(file.Title) ? file.FileNameNoPath : file.Title);
                title.SetXalign(0);
                title.AddCssClass("heading");
                var meta = Gtk.Label.New($"{file.FileNameNoPath}   {file.Author}   {(file.LastPlayed.HasValue ? file.LastPlayed.Value.ToShortDateString() : "")}");
                meta.SetXalign(0);
                meta.AddCssClass("dim-label");
                left.Hexpand = true;
                left.Append(title);
                left.Append(meta);
                box.Append(left);
                if (file.Rating.HasValue)
                    box.Append(Gtk.Label.New($"★ {file.Rating.Value:0.0}"));
                row.SetChild(box);
                m_list.Append(row);

                var tile = Gtk.FlowBoxChild.New();
                tile.Name = row.Name;
                var tileBox = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
                tileBox.SetMarginStart(6);
                tileBox.SetMarginEnd(6);
                tileBox.SetMarginTop(6);
                tileBox.SetMarginBottom(6);
                var pic = Gtk.Picture.New();
                pic.SetContentFit(Gtk.ContentFit.Cover);
                int size = Math.Max(120, DataCache.Instance.AppConfiguration.TileImageSize / 2);
                pic.SetSizeRequest(size, (int)(size * 0.75));
                var image = m_ops.GetMainImage(file);
                GtkUtil.SetPictureFromFile(pic, image?.FullFileName);
                var tileTitle = Gtk.Label.New(string.IsNullOrEmpty(file.Title) ? file.FileNameNoPath : file.Title);
                tileTitle.SetEllipsize(Pango.EllipsizeMode.End);
                tileBox.Append(pic);
                tileBox.Append(tileTitle);
                tile.SetChild(tileBox);
                m_tiles.Append(tile);
            }
        }

        private void ToggleView()
        {
            string visible = m_viewStack.GetVisibleChildName();
            m_viewStack.SetVisibleChildName(visible == "list" ? "tiles" : "list");
        }

        private void ApplyViewType(GameFileViewType type)
        {
            m_viewStack.SetVisibleChildName(type == GameFileViewType.GridView ? "list" : "tiles");
        }

        private void OnSelectionChanged()
        {
            m_selected.Clear();
            for (var child = m_list.GetFirstChild(); child != null; child = child.GetNextSibling())
            {
                var row = child as Gtk.ListBoxRow;
                if (row != null && row.IsSelected())
                {
                    var file = FileFromKey(row.Name);
                    if (file != null)
                        m_selected.Add(file);
                }
            }
            UpdateSummary();
        }

        private void OnTileSelectionChanged()
        {
            m_selected.Clear();
            for (var child = m_tiles.GetFirstChild(); child != null; child = child.GetNextSibling())
            {
                var tile = child as Gtk.FlowBoxChild;
                if (tile != null && tile.IsSelected())
                {
                    var file = FileFromKey(tile.Name);
                    if (file != null)
                        m_selected.Add(file);
                }
            }
            UpdateSummary();
        }

        private IGameFile FileFromKey(string key)
        {
            if (int.TryParse(key, out int id))
                return m_currentFiles.FirstOrDefault(x => x.GameFileID == id);
            return m_currentFiles.FirstOrDefault(x => x.FileName == key);
        }

        private IGameFile SelectedFile() => m_selected.FirstOrDefault() ?? m_currentFiles.FirstOrDefault();
        private IGameFile[] SelectedFiles() => m_selected.Count > 0 ? m_selected.ToArray() : Array.Empty<IGameFile>();

        private void UpdateSummary()
        {
            var file = SelectedFile();
            if (file == null)
            {
                m_summaryTitle.SetLabel("Select a file");
                m_summaryMeta.SetLabel(string.Empty);
                m_summaryDesc.SetLabel(string.Empty);
                ClearAssoc(m_screenshots);
                ClearAssoc(m_saves);
                ClearAssoc(m_demos);
                return;
            }

            m_summaryTitle.SetLabel(string.IsNullOrEmpty(file.Title) ? file.FileNameNoPath : file.Title);
            m_summaryMeta.SetLabel($"{file.Author}\n{file.FileNameNoPath}\nMaps: {file.Map}\nLast played: {file.LastPlayed}\nPlayed: {Util.GetTimePlayedString(file.MinutesPlayed)}");
            m_summaryDesc.SetLabel(file.Description ?? string.Empty);
            var image = m_ops.GetMainImage(file);
            GtkUtil.SetPictureFromFile(m_summaryImage, image?.FullFileName);

            FillAssoc(m_screenshots, m_ops.GetAssociationFiles(file, FileType.Screenshot));
            FillAssoc(m_saves, m_ops.GetAssociationFiles(file, FileType.SaveGame));
            FillAssoc(m_demos, m_ops.GetAssociationFiles(file, FileType.Demo));
        }

        private void ClearAssoc(Gtk.ListBox list)
        {
            GtkUtil.ClearList(list);
        }

        private void FillAssoc(Gtk.ListBox list, IEnumerable<IFileData> files)
        {
            ClearAssoc(list);
            foreach (var file in files)
            {
                var row = Gtk.ListBoxRow.New();
                row.Name = file.FileID?.ToString() ?? file.FileName;
                row.SetChild(Gtk.Label.New(file.Title ?? file.FileName));
                list.Append(row);
            }
        }

        private IFileData CurrentAssociation()
        {
            var file = SelectedFile();
            if (file == null)
                return null;
            var all = m_ops.GetAssociationFiles(file, FileType.Screenshot)
                .Concat(m_ops.GetAssociationFiles(file, FileType.SaveGame))
                .Concat(m_ops.GetAssociationFiles(file, FileType.Demo));
            Gtk.ListBox list = m_screenshots.GetSelectedRow() != null ? m_screenshots
                : m_saves.GetSelectedRow() != null ? m_saves : m_demos;
            var row = list.GetSelectedRow();
            if (row == null)
                return all.FirstOrDefault();
            return all.FirstOrDefault(x => (x.FileID?.ToString() ?? x.FileName) == row.Name);
        }

        private void ImportCurrentAssociation()
        {
            var gameFile = SelectedFile();
            if (gameFile == null)
                return;
            GtkUtil.OpenFiles(this, "Import files", new[] { "*" }, files =>
            {
                foreach (var path in files)
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    FileType type = ext switch
                    {
                        ".lmp" => FileType.Demo,
                        ".zds" or ".dsg" or ".save" => FileType.SaveGame,
                        _ => FileType.Screenshot
                    };
                    m_ops.ImportAssociationFile(gameFile, type, path, null);
                }
                UpdateSummary();
            });
        }

        private void OpenCurrentAssociation()
        {
            var file = CurrentAssociation();
            if (file == null)
                return;
            string path = file.FullFileName ?? file.FileName;
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp")
                ScreenshotViewerDialog.Show(this, path, SelectedFile());
            else
                GtkUtil.OpenPath(path);
        }

        private void EditCurrentAssociation()
        {
            var file = CurrentAssociation();
            if (file == null)
                return;
            ScreenshotEditDialog.Show(this, file, () =>
            {
                m_ops.UpdateAssociationFile(file);
                UpdateSummary();
            });
        }

        private void DeleteCurrentAssociation()
        {
            var file = CurrentAssociation();
            if (file == null)
                return;
            GtkUtil.Confirm(this, "Delete", $"Delete {file.FileName}?", ok =>
            {
                if (!ok)
                    return;
                m_ops.DeleteAssociationFile(file);
                UpdateSummary();
            });
        }

        private void SetMainScreenshot()
        {
            var file = CurrentAssociation();
            if (file == null || file.FileTypeID != FileType.Screenshot)
                return;
            var gameFile = SelectedFile();
            foreach (var shot in m_ops.GetAssociationFiles(gameFile, FileType.Screenshot))
            {
                shot.IsMain = shot.FileID == file.FileID;
                m_ops.UpdateAssociationFile(shot);
            }
            UpdateSummary();
        }

        private void HandlePlay(bool PlayForceDialog)
        {
            var files = SelectedFiles();
            var gameFile = files.FirstOrDefault();
            if (gameFile == null && CurrentTab()?.Kind != LibraryTabKind.IdGames)
            {
                var iwadId = (int)DataCache.Instance.AppConfiguration.GetTypedConfigValue(ConfigType.DefaultIWad, typeof(int));
                var iwad = DataCache.Instance.DataSourceAdapter.GetIWadByIWadID(iwadId);
                if (iwad?.GameFileID != null)
                {
                    var options = new GameFileGetOptions(new GameFileSearchField(GameFileFieldType.GameFileID, iwad.GameFileID.Value.ToString()));
                    gameFile = DataCache.Instance.DataSourceAdapter.GetGameFiles(options).FirstOrDefault();
                }
            }

            if (gameFile == null)
            {
                Toast("Select a file to play.");
                return;
            }

            if (CurrentTab()?.Kind == LibraryTabKind.IdGames)
            {
                DownloadSelected();
                return;
            }

            bool autoPlay = !PlayForceDialog && !DataCache.Instance.AppConfiguration.ShowPlayDialog;
            if (autoPlay)
            {
                LaunchFromProfile(gameFile, null);
                return;
            }

            PlayDialog.Show(this, gameFile, m_ops.HasActiveSessions, request =>
            {
                var result = m_ops.Launch(request);
                if (result.Failed)
                    GtkUtil.Alert(this, "Launch failed", result.ErrorMessage);
                else
                {
                    m_closingAfterPlay = m_launchArgs.AutoClose;
                    Toast($"Launched {gameFile.Title}");
                }
            });
        }

        private void LaunchFromProfile(IGameFile gameFile, PlayLaunchRequest preset)
        {
            var full = DataCache.Instance.DataSourceAdapter.GetGameFile(gameFile.FileName) ?? gameFile;
            if (full is IGameProfile profile)
                GameProfile.ApplyDefaultsToProfile(profile, DataCache.Instance.AppConfiguration);
            var request = preset ?? new PlayLaunchRequest
            {
                GameFile = full,
                SelectedGameProfile = full as IGameProfile,
                SelectedMap = full.SettingsMap,
                SelectedSkill = full.SettingsSkill,
                ExtraParameters = full.SettingsExtraParams,
                ExtraParametersOnly = full.SettingsExtraParamsOnly,
                SaveStatistics = full.SettingsStat,
                LoadLatestSave = full.SettingsLoadLatestSave,
                Remember = true
            };
            if (full.SourcePortID.HasValue)
                request.SelectedSourcePort = DataCache.Instance.DataSourceAdapter.GetSourcePort(full.SourcePortID.Value);
            if (full.IWadID.HasValue)
            {
                var iwad = DataCache.Instance.DataSourceAdapter.GetIWads().FirstOrDefault(x => x.IWadID == full.IWadID.Value);
                if (iwad?.GameFileID != null)
                {
                    var options = new GameFileGetOptions(new GameFileSearchField(GameFileFieldType.GameFileID, iwad.GameFileID.Value.ToString()));
                    request.SelectedIWad = DataCache.Instance.DataSourceAdapter.GetGameFiles(options).FirstOrDefault();
                }
            }
            var handler = new FileLoadHandler(DataCache.Instance.DataSourceAdapter, full);
            request.AdditionalFiles = handler.GetCurrentAdditionalFiles();
            var result = m_ops.Launch(request);
            if (result.Failed)
                GtkUtil.Alert(this, "Launch failed", result.ErrorMessage);
        }

        private void AddFiles(string[] files, AddFileType type, Action<SyncResult> done = null)
        {
            if (files == null || files.Length == 0)
            {
                done?.Invoke(SyncResult.EMPTY);
                return;
            }
            files = m_ops.ExpandZdlFiles(files);
            FileManagement management = DataCache.Instance.AppConfiguration.FileManagement;
            if (management == FileManagement.Prompt)
            {
                FileManagementDialog.Show(this, chosen => DoAdd(files, type, chosen, done));
                return;
            }
            DoAdd(files, type, management, done);
        }

        private void DoAdd(string[] files, AddFileType type, FileManagement management, Action<SyncResult> done = null)
        {
            Task.Run(() =>
            {
                var result = m_ops.AddAndSync(files, type, management, true, null);
                GtkUtil.RunOnUi(() =>
                {
                    m_progress.Visible = false;
                    ReloadCurrentTab();
                    if (type == AddFileType.IWad)
                        SourcePortSetup.EnsureDefaultIWad(DataCache.Instance.DataSourceAdapter);
                    if (result.InvalidFiles.Count > 0)
                        SyncStatusDialog.Show(this, result);
                    else
                        Toast($"Added {result.AddedGameFiles.Count} file(s)");
                    MaybePlayImported(result);
                    done?.Invoke(result);
                });
            });
        }

        private void AddRecursive(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return;
            var extensions = Util.GetPkExtenstions().Union(Util.GetDehackedExtensions()).Append(".wad").Select(x => "*" + x).ToList();
            IEnumerable<string> files = Array.Empty<string>();
            foreach (var ext in extensions)
                files = files.Union(Directory.EnumerateFiles(folder, ext, SearchOption.AllDirectories));
            AddFiles(files.ToArray(), AddFileType.GameFile);
        }

        private async void LoadStores()
        {
            var check = new AutomaticGameStoreCheck(StoreGameLoader.LoadAllStoreGamesFromRegistry, DbDataSourceAdapter.CreateAdapter());
            await check.LoadGamesFromGameStores(
                async iwads => { AddFiles(iwads.ToArray(), AddFileType.IWad); await Task.CompletedTask; },
                async pwads => { AddFiles(pwads.ToArray(), AddFileType.GameFile); await Task.CompletedTask; },
                async exe => { m_ops.AddDoom64SourcePort(exe); await Task.CompletedTask; });
            ReloadCurrentTab();
            Toast("Finished loading Steam/GOG/Heroic/Lutris titles");
        }

        private void CreateZip()
        {
            GtkUtil.OpenFolder(this, "Folder to zip", folder =>
            {
                if (string.IsNullOrEmpty(folder))
                    return;
                GtkUtil.SaveFile(this, "Save zip", Path.GetFileName(folder) + ".zip", dest =>
                {
                    if (string.IsNullOrEmpty(dest))
                        return;
                    bool ok = LibraryOperations.CreateZipFromDirectory(folder, dest);
                    Toast(ok ? "Zip created" : "Failed to create zip");
                });
            });
        }

        private void ViewText()
        {
            var file = SelectedFile();
            if (file == null)
                return;
            var names = m_ops.ListTextFiles(file);
            if (names.Count == 0)
            {
                GtkUtil.Alert(this, "Error", "No text file found.");
                return;
            }
            if (names.Count == 1)
            {
                var path = m_ops.ExtractTextFile(file, names[0]);
                GtkUtil.OpenPath(path);
                return;
            }
            SimpleSelectDialog.Show(this, "Select text file", names, name =>
            {
                var path = m_ops.ExtractTextFile(file, name);
                GtkUtil.OpenPath(path);
            });
        }

        private void OpenArchive()
        {
            var file = SelectedFile();
            if (file == null)
                return;
            GtkUtil.OpenPath(m_ops.GetGameFilePath(file));
        }

        private void EditSelected()
        {
            var files = SelectedFiles();
            if (files.Length == 0)
                return;
            GameFileEditDialog.Show(this, files, ReloadCurrentTab);
        }

        private void ResyncSelected(bool titlepic)
        {
            var files = SelectedFiles();
            if (files.Length == 0)
                return;
            var type = CurrentTab()?.Kind == LibraryTabKind.IWads ? AddFileType.IWad : AddFileType.GameFile;
            Task.Run(() =>
            {
                m_ops.Resync(files, type, titlepic);
                GtkUtil.RunOnUi(() =>
                {
                    ReloadCurrentTab();
                    Toast("Resync complete");
                });
            });
        }

        private void HandleResyncRecommended()
        {
            var files = m_ops.GetSyncNeeded().ToArray();
            if (files.Length == 0)
            {
                m_resyncButton.Visible = false;
                return;
            }
            ResyncSelected(true);
            m_resyncButton.Visible = false;
        }

        private void UpdateMetadata()
        {
            var files = SelectedFiles();
            if (files.Length == 0)
                return;
            MetadataDialog.Show(this, files, m_idGames, ReloadCurrentTab);
        }

        private void TagSelected()
        {
            var files = SelectedFiles();
            if (files.Length == 0)
                return;
            TagSelectDialog.Show(this, chosen =>
            {
                foreach (var file in files)
                    DataCache.Instance.AddGameFileTag(new[] { file }, chosen, out _);
                DataCache.Instance.TagMapLookup.Refresh(new[] { chosen });
                RebuildTabs();
                ReloadCurrentTab();
            });
        }

        private void RunUtility()
        {
            var file = SelectedFile();
            if (file == null)
                return;
            SourcePortsDialog.Show(this, SourcePortLaunchType.Utility, () =>
            {
                var util = DataCache.Instance.DataSourceAdapter.GetUtilities().FirstOrDefault();
                if (util == null)
                    return;
                var request = new PlayLaunchRequest
                {
                    GameFile = file,
                    SelectedSourcePort = util,
                    Remember = false
                };
                var result = m_ops.Launch(request);
                if (result.Failed)
                    GtkUtil.Alert(this, "Utility failed", result.ErrorMessage);
            });
        }

        private void DeleteSelected()
        {
            var files = SelectedFiles();
            if (files.Length == 0)
                return;
            GtkUtil.Confirm(this, "Confirm", $"Delete {files.Length} file(s) and associated data?", ok =>
            {
                if (!ok)
                    return;
                foreach (var file in files)
                    m_ops.DeleteGameFile(file);
                ReloadCurrentTab();
            });
        }

        private void RenameSelected()
        {
            var file = SelectedFile();
            if (file == null)
                return;
            TextPromptDialog.Show(this, "Rename", file.FileNameNoPath, name =>
            {
                if (string.IsNullOrWhiteSpace(name))
                    return;
                try
                {
                    m_ops.RenameGameFile(file, name);
                    ReloadCurrentTab();
                }
                catch (Exception ex)
                {
                    GtkUtil.Alert(this, "Rename failed", ex.Message);
                }
            });
        }

        private void CreateShortcut(bool autoPlay)
        {
            foreach (var file in SelectedFiles())
                m_ops.CreateDesktopShortcut(file, autoPlay);
            Toast("Desktop shortcut created");
        }

        private void QueueDownload(IGameFileDownloadable file, bool playWhenReady)
        {
            if (file == null)
                return;
            m_playAfterDownload = playWhenReady;
            m_playAfterDownloadName = file.FileName;
            m_downloads.Present();
            m_downloadHandler.Download(m_idGames, file);
        }

        private void DownloadSelected()
        {
            var file = SelectedFile() as IGameFileDownloadable;
            if (file == null)
            {
                Toast("Select an idgames file to download");
                return;
            }
            QueueDownload(file, false);
        }

        private void OnItemDownloadCompleted(object sender, DownloadItemCompletedEventArgs e)
        {
            GtkUtil.RunOnUi(() =>
            {
                if (e == null || e.Cancelled || e.Error != null || string.IsNullOrEmpty(e.FilePath) || !File.Exists(e.FilePath))
                    return;
                AddFiles(new[] { e.FilePath }, AddFileType.GameFile);
            });
        }

        private void MaybePlayImported(SyncResult result)
        {
            if (!m_playAfterDownload || result == null)
                return;
            var match = result.AddedOrUpdatedFiles.FirstOrDefault(x =>
                string.Equals(x.FileNameNoPath, Path.GetFileName(m_playAfterDownloadName), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.FileName, m_playAfterDownloadName, StringComparison.OrdinalIgnoreCase));
            m_playAfterDownload = false;
            m_playAfterDownloadName = null;
            if (match != null)
                LaunchFromProfile(match, null);
        }

        private void OpenSetupWizard()
        {
            SetupWizard.Show(new SetupWizardCallbacks
            {
                Window = this,
                IdGames = m_idGames,
                Reload = ReloadCurrentTab,
                LoadStores = LoadStores,
                AddFiles = (files, type, done) => AddFiles(files, type, done),
                Download = QueueDownload,
                Play = file => LaunchFromProfile(file, null)
            });
        }

        private void OnShown()
        {
            var adapter = DataCache.Instance.DataSourceAdapter;
            bool needsWizard = !adapter.GetSourcePorts().Any() || !adapter.GetIWads().Any();
            SourcePortSetup.EnsureDetectedPorts(adapter);
            SourcePortSetup.EnsureDefaultSourcePort(adapter);

            if (needsWizard)
            {
                if (!adapter.GetIWads().Any())
                    LoadStores();
                OpenSetupWizard();
            }

            if (m_ops.GetSyncNeeded().Any())
                m_resyncButton.Visible = true;

            _ = CheckUpdate();
            HandleLaunchArgs();
            ReloadCurrentTab();
        }

        private void ViewWebPage()
        {
            if (SelectedFile() is IdGamesGameFile id)
            {
                var options = new GameFileGetOptions(new GameFileSearchField(GameFileFieldType.GameFileID, id.id.ToString()));
                var full = m_idGames.GetGameFiles(options).FirstOrDefault() as IdGamesGameFile;
                var url = m_ops.GetIdGamesWebUrl(full ?? id);
                GtkUtil.OpenPath(url);
            }
        }

        private void PlayRandom()
        {
            PlayRandomDialog.Show(this, m_currentFiles, (file, map, showDialog) =>
            {
                m_selected.Clear();
                m_selected.Add(file);
                if (showDialog)
                    HandlePlay(true);
                else
                    LaunchFromProfile(file, new PlayLaunchRequest { GameFile = file, SelectedMap = map, Remember = false });
            });
        }

        private void ShowAbout()
        {
            var about = Adw.AboutWindow.New();
            about.SetTransientFor(this);
            about.ApplicationName = "Doom Launcher";
            about.DeveloperName = "DoomLauncher / DoomedLauncherNix";
            about.Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "3.8";
            about.Comments = "Linux GTK 4 / Adwaita port of Doom Launcher with feature parity of the Windows frontend.";
            about.Website = Util.GitHubRepository;
            about.IssueUrl = Util.GitHubRepository + "/issues";
            about.Present();
        }

        private void OpenHelp()
        {
            string help = Path.Combine(AppContext.BaseDirectory, "Help.pdf");
            if (File.Exists(help))
                GtkUtil.OpenPath(help);
            else
                GtkUtil.OpenPath("https://github.com/nstlaurent/DoomLauncher");
        }

        private void ManualUpdate()
        {
            GtkUtil.OpenFile(this, "Select update zip", path =>
            {
                if (string.IsNullOrEmpty(path))
                    return;
                var updater = new ApplicationUpdater(path, AppContext.BaseDirectory);
                if (!updater.Execute())
                    GtkUtil.Alert(this, "Update failed", updater.LastError);
                else
                    Toast("Update applied. Restart Doom Launcher.");
            });
        }

        private void ShowUpdate()
        {
            if (m_updateInfo == null)
                return;
            GtkUtil.Confirm(this, "Update available",
                $"Version {m_updateInfo.Version} is available. Open the release page?",
                ok => { if (ok) GtkUtil.OpenPath(m_updateInfo.ReleasePageUrl); });
        }

        private void Toast(string text)
        {
            m_toasts.AddToast(Adw.Toast.New(text));
        }

        private async Task CheckUpdate()
        {
            try
            {
                var updater = new ApplicationUpdate(TimeSpan.FromSeconds(8));
                var info = await updater.GetUpdateApplicationInfo(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(3, 8, 0, 0));
                if (info != null)
                {
                    m_updateInfo = info;
                    GtkUtil.RunOnUi(() => m_updateButton.Visible = true);
                }
            }
            catch
            {
            }
        }

        private void HandleLaunchArgs()
        {
            if (!string.IsNullOrEmpty(m_launchArgs.LaunchFileName))
            {
                var existing = DataCache.Instance.DataSourceAdapter.GetGameFile(m_launchArgs.LaunchFileName);
                if (existing != null)
                {
                    m_selected.Clear();
                    m_selected.Add(existing);
                    HandlePlay(false);
                }
                else if (File.Exists(m_launchArgs.LaunchFileName))
                    AddFiles(new[] { m_launchArgs.LaunchFileName }, AddFileType.GameFile);
            }

            if (m_launchArgs.LaunchGameFileID.HasValue)
            {
                var options = new GameFileGetOptions(new GameFileSearchField(GameFileFieldType.GameFileID, m_launchArgs.LaunchGameFileID.Value.ToString()));
                var file = DataCache.Instance.DataSourceAdapter.GetGameFiles(options).FirstOrDefault();
                if (file != null)
                {
                    m_selected.Clear();
                    m_selected.Add(file);
                    HandlePlay(false);
                }
            }
        }

        public void SaveWindowConfig()
        {
            try
            {
                Native.GetDefaultSize(out int w, out int h);
                SetConfig("AppWidth", w.ToString());
                SetConfig("AppHeight", h.ToString());
                SetConfig("LastSelectedTabIndex", m_tabs.GetCurrentPage().ToString());
            }
            catch
            {
            }
        }

        private static void SetConfig(string name, string value)
        {
            var item = DataCache.Instance.DataSourceAdapter.GetConfiguration().FirstOrDefault(x => x.Name == name);
            if (item == null)
                return;
            item.Value = value;
            DataCache.Instance.DataSourceAdapter.UpdateConfiguration(item);
        }
    }
}
