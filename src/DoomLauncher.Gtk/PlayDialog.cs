using DoomLauncher.DataSources;
using DoomLauncher.Handlers;
using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DoomLauncher.Linux
{
    public static class PlayDialog
    {
        public static void Show(Gtk.Window parent, IGameFile gameFile, bool sessionInProgress, Action<PlayLaunchRequest> launched)
        {
            var adapter = DataCache.Instance.DataSourceAdapter;
            var full = adapter.GetGameFile(gameFile.FileName) ?? gameFile;
            if (full is IGameProfile profile)
                GameProfile.ApplyDefaultsToProfile(profile, DataCache.Instance.AppConfiguration);

            var window = GtkUtil.ModalWindow(parent, "Launch - " + (string.IsNullOrEmpty(full.Title) ? full.FileName : full.Title), 640, 720);
            var box = Gtk.Box.New(Gtk.Orientation.Vertical, 10);
            box.SetMarginStart(16);
            box.SetMarginEnd(16);
            box.SetMarginTop(16);
            box.SetMarginBottom(16);

            var profiles = adapter.GetGlobalGameProfiles().Concat(adapter.GetGameProfiles(full.GameFileID ?? 0)).Cast<IGameProfile>().ToList();
            if (full is IGameProfile asProfile && !profiles.Any(x => x.GameProfileID == asProfile.GameProfileID))
                profiles.Insert(0, asProfile);
            var profileNames = profiles.Select(x => x.Name ?? GameFile.DefaultProfileName).ToList();
            var profileDrop = GtkUtil.DropDownFrom(profileNames);

            var ports = (full.IsDoom64 ? adapter.GetDoom64() : adapter.GetSourcePorts()).ToList();
            var portDrop = GtkUtil.DropDownFrom(ports.Select(x => x.Name).DefaultIfEmpty("N/A").ToList());
            var iwads = Util.GetIWadsDataSource(adapter);
            var iwadDrop = GtkUtil.DropDownFrom(iwads.Select(x => x.FileName).DefaultIfEmpty("N/A").ToList());

            var maps = string.IsNullOrEmpty(full.Map) ? new List<string> { "" } : GameFile.GetMaps(full).ToList();
            if (!maps.Contains(""))
                maps.Insert(0, "");
            var mapDrop = GtkUtil.DropDownFrom(maps);
            var mapCheck = Gtk.CheckButton.NewWithLabel("Warp to map");
            var skills = Util.GetSkills().ToList();
            var skillDrop = GtkUtil.DropDownFrom(skills, Math.Max(0, skills.IndexOf(string.IsNullOrEmpty(full.SettingsSkill) ? "3" : full.SettingsSkill)));

            var demos = adapter.GetFiles(full, FileType.Demo).ToList();
            var demoDrop = GtkUtil.DropDownFrom(demos.Select(x => x.FileName).DefaultIfEmpty("(none)").ToList());
            var playDemo = Gtk.CheckButton.NewWithLabel("Play demo");
            var record = Gtk.CheckButton.NewWithLabel("Record demo");
            var extra = Gtk.Entry.New();
            extra.SetText(full.SettingsExtraParams ?? string.Empty);
            extra.SetPlaceholderText("Extra parameters");
            var extraOnly = Gtk.CheckButton.NewWithLabel("Extra parameters only");
            extraOnly.SetActive(full.SettingsExtraParamsOnly);
            var stats = Gtk.CheckButton.NewWithLabel("Save statistics");
            stats.SetActive(full.SettingsStat);
            var loadSave = Gtk.CheckButton.NewWithLabel("Load latest save");
            loadSave.SetActive(full.SettingsLoadLatestSave);
            var filter = Gtk.CheckButton.NewWithLabel("Screen filter (stored, compositor overlay not used on Linux)");
            var remember = Gtk.CheckButton.NewWithLabel("Remember settings");
            remember.SetActive(true);

            var filesList = Gtk.ListBox.New();
            filesList.AddCssClass("boxed-list");
            var handler = new FileLoadHandler(adapter, full);
            var additional = handler.GetCurrentAdditionalFiles();
            string[] specific = string.IsNullOrEmpty(full.SettingsSpecificFiles)
                ? null
                : full.SettingsSpecificFiles.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            void refreshFiles()
            {
                GtkUtil.ClearList(filesList);
                foreach (var f in additional)
                    filesList.Append(Gtk.Label.New(f.FileNameNoPath));
            }
            refreshFiles();

            if (full.SourcePortID.HasValue)
            {
                var selectedPort = ports.FirstOrDefault(x => x.SourcePortID == full.SourcePortID.Value);
                if (selectedPort != null)
                    GtkUtil.SetDropDownText(portDrop, selectedPort.Name);
            }
            if (full.IWadID.HasValue)
            {
                var selectedIwad = iwads.FirstOrDefault(x => x.IWadID == full.IWadID.Value);
                if (selectedIwad != null)
                    GtkUtil.SetDropDownText(iwadDrop, selectedIwad.FileName);
            }
            GtkUtil.SetDropDownText(mapDrop, full.SettingsMap);

            box.Append(GtkUtil.LabeledRow("Profile", profileDrop));
            box.Append(GtkUtil.LabeledRow("Source port", portDrop));
            box.Append(GtkUtil.LabeledRow("IWAD", iwadDrop));
            box.Append(mapCheck);
            box.Append(GtkUtil.LabeledRow("Map", mapDrop));
            box.Append(GtkUtil.LabeledRow("Skill", skillDrop));
            box.Append(playDemo);
            box.Append(GtkUtil.LabeledRow("Demo", demoDrop));
            box.Append(record);
            box.Append(GtkUtil.LabeledRow("Extra params", extra));
            box.Append(extraOnly);
            box.Append(stats);
            box.Append(loadSave);
            box.Append(filter);
            box.Append(remember);
            box.Append(Gtk.Label.New("Additional files"));
            box.Append(GtkUtil.Scroll(filesList));

            var addFile = GtkUtil.Button("Add file...", () =>
            {
                FileSelectDialog.Show(parent, selected =>
                {
                    additional.Add(selected);
                    handler.SetUserAdditionalFiles(additional);
                    refreshFiles();
                });
            });
            var specificButton = GtkUtil.Button("Specific files...", () =>
            {
                SpecificFilesDialog.Show(parent, full, chosen => specific = chosen);
            });
            var preview = GtkUtil.Button("Preview launch parameters", () =>
            {
                var req = BuildRequest();
                var ops = new LibraryOperations();
                GtkUtil.Alert(window, "Launch parameters", ops.PreviewLaunchParameters(req) ?? string.Empty);
            });
            box.Append(addFile);
            box.Append(specificButton);
            box.Append(preview);

            PlayLaunchRequest BuildRequest()
            {
                var portName = GtkUtil.GetDropDownText(portDrop);
                var iwadName = GtkUtil.GetDropDownText(iwadDrop);
                var port = ports.FirstOrDefault(x => x.Name == portName);
                var iwadData = iwads.FirstOrDefault(x => x.FileName == iwadName);
                IGameFile iwadFile = null;
                if (iwadData?.GameFileID != null)
                {
                    var options = new GameFileGetOptions(new GameFileSearchField(GameFileFieldType.GameFileID, iwadData.GameFileID.Value.ToString()));
                    iwadFile = adapter.GetGameFiles(options).FirstOrDefault();
                }
                var demoName = GtkUtil.GetDropDownText(demoDrop);
                return new PlayLaunchRequest
                {
                    GameFile = full,
                    SelectedGameProfile = profiles.ElementAtOrDefault((int)profileDrop.GetSelected()) ?? full as IGameProfile,
                    SelectedSourcePort = port,
                    SelectedIWad = iwadFile,
                    SelectedMap = mapCheck.GetActive() ? GtkUtil.GetDropDownText(mapDrop) : null,
                    SelectedSkill = GtkUtil.GetDropDownText(skillDrop),
                    AdditionalFiles = additional.ToList(),
                    SpecificFiles = specific,
                    ExtraParameters = extra.GetText(),
                    ExtraParametersOnly = extraOnly.GetActive(),
                    Record = record.GetActive(),
                    PlayDemo = playDemo.GetActive(),
                    SelectedDemo = demos.FirstOrDefault(x => x.FileName == demoName),
                    SaveStatistics = stats.GetActive(),
                    LoadLatestSave = loadSave.GetActive(),
                    Remember = remember.GetActive(),
                    ScreenFilter = filter.GetActive()
                };
            }

            var launch = Gtk.Button.NewWithLabel("Launch");
            launch.AddCssClass("suggested-action");
            launch.OnClicked += (s, e) =>
            {
                launched(BuildRequest());
                window.Destroy();
            };
            var cancel = GtkUtil.Button("Cancel", () => window.Destroy());
            box.Append(GtkUtil.DialogButtons(cancel, launch));

            window.SetChild(GtkUtil.Scroll(box));
            window.Present();
        }
    }
}
