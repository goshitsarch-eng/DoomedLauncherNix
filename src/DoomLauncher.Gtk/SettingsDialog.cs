using DoomLauncher.Config;
using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DoomLauncher.Linux
{
    public static class SettingsDialog
    {
        public static void Show(Gtk.Window parent, Action saved) => Show(parent, false, saved);

        public static void ShowLaunchDefaults(Gtk.Window parent, Action saved) => Show(parent, true, saved);

        private static void Show(Gtk.Window parent, bool launchTab, Action saved)
        {
            var adapter = DataCache.Instance.DataSourceAdapter;
            var window = GtkUtil.ModalWindow(parent, "Settings", 560, 640);
            var notebook = Gtk.Notebook.New();

            var general = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            general.SetMarginStart(12);
            general.SetMarginEnd(12);
            general.SetMarginTop(12);
            var widgets = new List<(IConfigurationData Config, Gtk.Widget Widget)>();
            foreach (var config in adapter.GetConfiguration().Where(x => x.UserCanModify))
            {
                Gtk.Widget widget;
                if (!string.IsNullOrEmpty(config.AvailableValues))
                {
                    var options = config.AvailableValues.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Split(',')[0]).ToList();
                    var drop = GtkUtil.DropDownFrom(options);
                    GtkUtil.SetDropDownText(drop, config.Value);
                    widget = drop;
                }
                else if (config.Value == "true" || config.Value == "false")
                {
                    var check = Gtk.CheckButton.New();
                    check.SetActive(string.Equals(config.Value, "true", StringComparison.OrdinalIgnoreCase));
                    widget = check;
                }
                else
                {
                    var entry = Gtk.Entry.New();
                    entry.SetText(config.Value ?? string.Empty);
                    widget = entry;
                }
                widgets.Add((config, widget));
                general.Append(GtkUtil.LabeledRow(SplitWords(config.Name), widget));
            }
            notebook.AppendPage(GtkUtil.Scroll(general), Gtk.Label.New("Configuration"));

            var launch = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            launch.SetMarginStart(12);
            launch.SetMarginEnd(12);
            launch.SetMarginTop(12);
            launch.Append(Gtk.Label.New("Default settings used when a game file has no saved configuration."));
            var ports = adapter.GetSourcePorts().ToList();
            var iwads = adapter.GetIWads().ToList();
            var portDrop = GtkUtil.DropDownFrom(ports.Select(x => x.Name).DefaultIfEmpty("N/A").ToList());
            var iwadDrop = GtkUtil.DropDownFrom(iwads.Select(x => x.Name).DefaultIfEmpty("N/A").ToList());
            var skillDrop = GtkUtil.DropDownFrom(Util.GetSkills().ToList(), 2);
            launch.Append(GtkUtil.LabeledRow("Default source port", portDrop));
            launch.Append(GtkUtil.LabeledRow("Default IWAD", iwadDrop));
            launch.Append(GtkUtil.LabeledRow("Default skill", skillDrop));
            notebook.AppendPage(launch, Gtk.Label.New("Launch defaults"));

            var views = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            views.SetMarginStart(12);
            views.SetMarginEnd(12);
            views.SetMarginTop(12);
            views.Append(Gtk.Label.New("Visible library tabs (Local is always shown). Restart view after changing."));
            var checks = new Dictionary<string, Gtk.CheckButton>();
            foreach (var key in TabKeys.KeyNames.Where(x => x != TabKeys.LocalKey))
            {
                var check = Gtk.CheckButton.NewWithLabel(StaticTagData.GetFavoriteName(key));
                check.SetActive(DataCache.Instance.AppConfiguration.VisibleViews.Contains(key));
                checks[key] = check;
                views.Append(check);
            }
            notebook.AppendPage(views, Gtk.Label.New("Views"));
            if (launchTab)
                notebook.SetCurrentPage(1);

            var save = Gtk.Button.NewWithLabel("Save");
            save.AddCssClass("suggested-action");
            save.OnClicked += (s, e) =>
            {
                foreach (var (config, widget) in widgets)
                {
                    if (widget is Gtk.Entry entry)
                        config.Value = entry.GetText();
                    else if (widget is Gtk.CheckButton check)
                        config.Value = check.GetActive() ? "true" : "false";
                    else if (widget is Gtk.DropDown drop)
                        config.Value = GtkUtil.GetDropDownText(drop);
                    adapter.UpdateConfiguration(config);
                }

                SetNamed(adapter, ConfigType.DefaultSourcePort.ToString("g"),
                    ports.FirstOrDefault(x => x.Name == GtkUtil.GetDropDownText(portDrop))?.SourcePortID.ToString());
                SetNamed(adapter, ConfigType.DefaultIWad.ToString("g"),
                    iwads.FirstOrDefault(x => x.Name == GtkUtil.GetDropDownText(iwadDrop))?.IWadID.ToString());
                SetNamed(adapter, ConfigType.DefaultSkill.ToString("g"), GtkUtil.GetDropDownText(skillDrop));
                SetNamed(adapter, AppConfiguration.VisibleViewsName, string.Join(";", checks.Where(x => x.Value.GetActive()).Select(x => x.Key)));

                DataCache.Instance.AppConfiguration.Refresh();
                GtkUtil.ApplyColorTheme(DataCache.Instance.AppConfiguration.ColorTheme);
                saved?.Invoke();
                window.Destroy();
            };

            var root = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
            notebook.Vexpand = true;
            root.Append(notebook);
            root.Append(GtkUtil.DialogButtons(GtkUtil.Button("Cancel", () => window.Destroy()), save));
            window.SetChild(root);
            window.Present();
        }

        private static void SetNamed(IDataSourceAdapter adapter, string name, string value)
        {
            if (value == null)
                return;
            var item = adapter.GetConfiguration().FirstOrDefault(x => x.Name == name);
            if (item == null)
                return;
            item.Value = value;
            adapter.UpdateConfiguration(item);
        }

        private static string SplitWords(string name)
        {
            var chars = new List<char>();
            foreach (char c in name)
            {
                if (char.IsUpper(c) && chars.Count > 0)
                    chars.Add(' ');
                chars.Add(c);
            }
            return new string(chars.ToArray());
        }
    }
}
