using DoomLauncher.Handlers;
using DoomLauncher.Handlers.Sync;
using DoomLauncher.Interfaces;
using DoomLauncher.SourcePort;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DoomLauncher.Linux
{
    public class SetupWizardCallbacks
    {
        public Gtk.Window Window { get; set; }
        public IdGamesDataAdapater IdGames { get; set; }
        public Action Reload { get; set; }
        public Action LoadStores { get; set; }
        public Action<string[], AddFileType, Action<SyncResult>> AddFiles { get; set; }
        public Action<IGameFileDownloadable, bool> Download { get; set; }
        public Action<IGameFile> Play { get; set; }
    }

    public static class SetupWizard
    {
        public static void Show(SetupWizardCallbacks host)
        {
            if (host?.Window == null)
                return;
            new SetupWizardUi(host).Present();
        }
    }

    internal class SetupWizardUi
    {
        private readonly SetupWizardCallbacks m_host;
        private readonly Gtk.Window m_window;
        private readonly Gtk.Stack m_stack;
        private readonly Gtk.Label m_step;
        private readonly Gtk.Button m_back;
        private readonly Gtk.Button m_next;
        private Gtk.Label m_portStatus;
        private Gtk.ListBox m_portList;
        private Gtk.Label m_iwadStatus;
        private Gtk.Label m_installLog;
        private Gtk.ProgressBar m_progress;
        private readonly string[] m_pages = { "welcome", "ports", "iwads", "mods", "done" };
        private int m_index;

        public SetupWizardUi(SetupWizardCallbacks host)
        {
            m_host = host;
            m_window = GtkUtil.ModalWindow(host.Window, "Setup assistant", 760, 680);

            var header = Adw.HeaderBar.New();
            header.SetShowEndTitleButtons(true);
            m_step = Gtk.Label.New("Step 1 of 5");
            m_step.AddCssClass("subtitle");
            header.SetTitleWidget(m_step);

            m_stack = Gtk.Stack.New();
            m_stack.SetTransitionType(Gtk.StackTransitionType.SlideLeftRight);
            m_stack.Vexpand = true;
            m_stack.AddTitled(WelcomePage(), "welcome", "Welcome");
            m_stack.AddTitled(PortsPage(), "ports", "GZDoom");
            m_stack.AddTitled(IwadsPage(), "iwads", "IWADs");
            m_stack.AddTitled(ModsPage(), "mods", "Mods");
            m_stack.AddTitled(DonePage(), "done", "Done");

            m_back = Gtk.Button.NewWithLabel("Back");
            m_next = Gtk.Button.NewWithLabel("Next");
            m_next.AddCssClass("suggested-action");
            m_back.OnClicked += (s, e) => ShowPage(m_index - 1);
            m_next.OnClicked += (s, e) =>
            {
                if (m_index >= m_pages.Length - 1)
                    m_window.Destroy();
                else
                    ShowPage(m_index + 1);
            };

            var buttons = GtkUtil.DialogButtons(
                GtkUtil.Button("Close", () => m_window.Destroy()),
                m_back,
                m_next);

            var body = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
            body.SetMarginStart(20);
            body.SetMarginEnd(20);
            body.SetMarginTop(12);
            body.SetMarginBottom(12);
            body.Append(m_stack);
            body.Append(buttons);

            var toolbar = Adw.ToolbarView.New();
            toolbar.AddTopBar(header);
            toolbar.SetContent(body);
            m_window.SetChild(toolbar);
            ShowPage(0);
            RefreshPorts();
            RefreshIwads();
        }

        public void Present() => m_window.Present();

        private void ShowPage(int index)
        {
            m_index = Math.Clamp(index, 0, m_pages.Length - 1);
            m_stack.SetVisibleChildName(m_pages[m_index]);
            m_step.SetLabel($"Step {m_index + 1} of {m_pages.Length}");
            m_back.Sensitive = m_index > 0;
            m_next.SetLabel(m_index == m_pages.Length - 1 ? "Finish" : "Next");
            if (m_pages[m_index] == "ports")
                RefreshPorts();
            if (m_pages[m_index] == "iwads")
                RefreshIwads();
        }

        private Gtk.Widget WelcomePage()
        {
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
            var title = Gtk.Label.New("Custom Doom, with less setup");
            title.AddCssClass("title-1");
            title.SetXalign(0);
            var text = Gtk.Label.New("This assistant finds GZDoom even when it is a Flatpak, imports Doom IWADs (or Freedoom), then helps you download wads from idgames and launch them.");
            text.SetWrap(true);
            text.SetXalign(0);
            box.Append(title);
            box.Append(text);
            box.Append(Bullet("Detect distro, PATH, Flatpak, and snap builds of GZDoom and other ports."));
            box.Append(Bullet("Install GZDoom from Flathub if nothing is on this computer yet."));
            box.Append(Bullet("Add IWADs from Steam/GOG/Heroic/Lutris, a file picker, or Freedoom."));
            box.Append(Bullet("Download featured mods and play them as soon as they land in your library."));
            return box;
        }

        private Gtk.Widget PortsPage()
        {
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            var title = Gtk.Label.New("GZDoom and other source ports");
            title.AddCssClass("title-2");
            title.SetXalign(0);
            box.Append(title);
            box.Append(Wrapped("Doom Launcher will use whatever it can find: a distro package named gzdoom, a binary on PATH, or Flatpak app org.zdoom.GZDoom. Flatpak launches as flatpak run --filesystem=host … so your library files are visible."));

            m_portStatus = Gtk.Label.New(string.Empty);
            m_portStatus.SetXalign(0);
            m_portStatus.SetWrap(true);
            box.Append(m_portStatus);

            m_portList = Gtk.ListBox.New();
            m_portList.AddCssClass("boxed-list");
            box.Append(GtkUtil.Scroll(m_portList));

            m_installLog = Gtk.Label.New(string.Empty);
            m_installLog.SetXalign(0);
            m_installLog.SetWrap(true);
            m_installLog.AddCssClass("dim-label");
            box.Append(m_installLog);

            var detect = GtkUtil.Button("Detect again", RefreshPorts);
            var install = GtkUtil.Button("Install GZDoom (Flatpak)", InstallGzdoom);
            var add = GtkUtil.Button("Add a port manually...", () =>
                SourcePortEditDialog.Show(m_window, SourcePortLaunchType.SourcePort, null, RefreshPorts));
            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            row.Append(detect);
            row.Append(install);
            row.Append(add);
            box.Append(row);
            return box;
        }

        private Gtk.Widget IwadsPage()
        {
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            var title = Gtk.Label.New("IWADs (the base game)");
            title.AddCssClass("title-2");
            title.SetXalign(0);
            box.Append(title);
            box.Append(Wrapped("GZDoom needs an IWAD such as DOOM2.WAD. Scan the stores this machine already has, pick files yourself, or download Freedoom if you do not own Doom."));

            m_iwadStatus = Gtk.Label.New(string.Empty);
            m_iwadStatus.SetXalign(0);
            m_iwadStatus.SetWrap(true);
            box.Append(m_iwadStatus);

            m_progress = Gtk.ProgressBar.New();
            m_progress.Visible = false;
            box.Append(m_progress);

            var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            row.Append(GtkUtil.Button("Scan Steam / GOG / Heroic / Lutris", () =>
            {
                m_host.LoadStores?.Invoke();
                GtkUtil.RunOnUi(() =>
                {
                    RefreshIwads();
                    m_host.Reload?.Invoke();
                });
            }));
            row.Append(GtkUtil.Button("Add IWAD files...", () =>
            {
                GtkUtil.OpenFiles(m_window, "Select IWADs", new[] { "*.wad", "*.iwad", "*.ipk3" }, files =>
                {
                    m_host.AddFiles?.Invoke(files, AddFileType.IWad, _ =>
                    {
                        RefreshIwads();
                        m_host.Reload?.Invoke();
                    });
                });
            }));
            row.Append(GtkUtil.Button("Download Freedoom", DownloadFreedoom));
            box.Append(row);
            return box;
        }

        private Gtk.Widget ModsPage()
        {
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            var title = Gtk.Label.New("Download something to play");
            title.AddCssClass("title-2");
            title.SetXalign(0);
            box.Append(title);
            box.Append(GetModsDialog.Create(m_window, m_host.IdGames, (file, play) =>
            {
                m_host.Download?.Invoke(file, play);
                m_host.Reload?.Invoke();
            }));
            return box;
        }

        private Gtk.Widget DonePage()
        {
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
            var title = Gtk.Label.New("You are ready to launch");
            title.AddCssClass("title-1");
            title.SetXalign(0);
            box.Append(title);
            box.Append(Wrapped("Use Play on any library item. The Id Games tab and Get mods… stay available whenever you want another wad. Re-open this assistant from the menu if you install GZDoom later or switch to a Flatpak build."));
            box.Append(GtkUtil.Button("Open Get mods…", () =>
                GetModsDialog.Show(m_window, m_host.IdGames, m_host.Download)));
            return box;
        }

        private void RefreshPorts()
        {
            var adapter = DataCache.Instance.DataSourceAdapter;
            var added = SourcePortSetup.EnsureDetectedPorts(adapter);
            var ports = adapter.GetSourcePorts().ToList();
            GtkUtil.ClearList(m_portList);
            foreach (var port in ports)
            {
                var row = Gtk.ListBoxRow.New();
                string extra = SourcePortLaunch.IsManaged(port.Executable) ? "  ·  sandboxed (Flatpak/snap)" : string.Empty;
                var ok = SourcePortLaunch.CanExecute(port);
                row.SetChild(Gtk.Label.New($"{port.Name}  ({port.Executable}){(ok ? extra : "  ·  missing")}"));
                m_portList.Append(row);
            }

            if (ports.Count == 0)
            {
                var hint = SourcePortDetector.GetGzdoomInstallHint();
                m_portStatus.SetLabel("No source port is configured yet. Install GZDoom or add a binary. Command:\n" + hint.Command);
            }
            else
            {
                var gz = ports.FirstOrDefault(p => SourcePortLaunch.IsZDoomFamily(p.Executable));
                m_portStatus.SetLabel(gz != null
                    ? $"Using {gz.Name} as your GZDoom-family port{(added.Count > 0 ? $". Added {added.Count} detected port(s)." : ".")}"
                    : $"{ports.Count} port(s) configured. Add GZDoom if you want the usual mod setup.");
            }
            if (added.Count > 0)
                m_host.Reload?.Invoke();
        }

        private void RefreshIwads()
        {
            var adapter = DataCache.Instance.DataSourceAdapter;
            SourcePortSetup.EnsureDefaultIWad(adapter);
            int count = adapter.GetIWads().Count();
            m_iwadStatus.SetLabel(count == 0
                ? "No IWADs yet. Scan a store, pick DOOM2.WAD, or download Freedoom."
                : $"Found {count} IWAD(s). You can add more at any time.");
        }

        private void InstallGzdoom()
        {
            var hint = SourcePortDetector.GetGzdoomInstallHint();
            m_installLog.SetLabel(hint.CanRun
                ? "Installing GZDoom from Flathub. This needs network access and may take a minute..."
                : "Flatpak is not on PATH. Run one of these in a terminal:\n" + hint.Command);
            if (!hint.CanRun)
                return;

            Task.Run(() =>
            {
                try
                {
                    using var proc = SourcePortDetector.StartInstall(hint);
                    if (proc == null)
                    {
                        GtkUtil.RunOnUi(() => m_installLog.SetLabel("Could not start the Flatpak installer."));
                        return;
                    }
                    string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    GtkUtil.RunOnUi(() =>
                    {
                        m_installLog.SetLabel(proc.ExitCode == 0
                            ? "GZDoom Flatpak installed. Detecting..."
                            : "Install did not finish. Output:\n" + TrimLog(output) + "\nYou can run:\n" + hint.Command);
                        RefreshPorts();
                    });
                }
                catch (Exception ex)
                {
                    GtkUtil.RunOnUi(() => m_installLog.SetLabel("Install failed: " + ex.Message + "\n" + hint.Command));
                }
            });
        }

        private void DownloadFreedoom()
        {
            var dest = DataCache.Instance.AppConfiguration.GameFileDirectory.GetFullPath();
            m_progress.Visible = true;
            m_progress.Pulse();
            m_iwadStatus.SetLabel("Downloading Freedoom...");
            var progress = new Progress<string>(text => GtkUtil.RunOnUi(() =>
            {
                m_iwadStatus.SetLabel(text);
                m_progress.Pulse();
            }));
            Task.Run(async () =>
            {
                var result = await FreedoomInstaller.InstallAsync(dest, progress, CancellationToken.None);
                GtkUtil.RunOnUi(() =>
                {
                    m_progress.Visible = false;
                    if (!result.Succeeded)
                    {
                        m_iwadStatus.SetLabel("Freedoom download failed: " + result.Error);
                        return;
                    }
                    m_host.AddFiles?.Invoke(result.WadPaths, AddFileType.IWad, _ =>
                    {
                        RefreshIwads();
                        m_host.Reload?.Invoke();
                    });
                });
            });
        }

        private static Gtk.Label Bullet(string text)
        {
            var label = Gtk.Label.New("•  " + text);
            label.SetWrap(true);
            label.SetXalign(0);
            return label;
        }

        private static Gtk.Label Wrapped(string text)
        {
            var label = Gtk.Label.New(text);
            label.SetWrap(true);
            label.SetXalign(0);
            return label;
        }

        private static string TrimLog(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            text = text.Trim();
            return text.Length <= 1200 ? text : text.Substring(text.Length - 1200);
        }
    }
}
