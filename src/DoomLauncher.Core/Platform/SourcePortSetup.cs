using DoomLauncher.DataSources;
using DoomLauncher.Interfaces;
using DoomLauncher.SourcePort;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DoomLauncher
{
    public static class SourcePortSetup
    {
        public static string DefaultSupportedExtensions()
        {
            return string.Join(",", new[] { ".wad" }.Union(Util.GetDehackedExtensions()).Union(Util.GetSourcePortPkExtensions()));
        }

        public static bool AlreadyConfigured(IEnumerable<ISourcePortData> existing, DetectedSourcePort detected)
        {
            if (existing == null || detected == null || string.IsNullOrEmpty(detected.Executable))
                return false;
            return existing.Any(port =>
                port != null &&
                string.Equals(port.Executable, detected.Executable, StringComparison.OrdinalIgnoreCase));
        }

        public static SourcePortData ToSourcePort(DetectedSourcePort detected)
        {
            if (detected == null)
                throw new ArgumentNullException(nameof(detected));

            return new SourcePortData
            {
                Name = detected.Name,
                Executable = detected.Executable,
                Directory = new LauncherPath(detected.Directory ?? string.Empty),
                SupportedExtensions = DefaultSupportedExtensions(),
                FileOption = "-file",
                ExtraParameters = string.Empty,
                LaunchType = SourcePortLaunchType.SourcePort,
                AltSaveDirectory = string.IsNullOrEmpty(detected.ConfigDirectory)
                    ? LauncherPath.NoPath
                    : new LauncherPath(detected.ConfigDirectory),
                Archived = false
            };
        }

        public static IReadOnlyList<ISourcePortData> EnsureDetectedPorts(IDataSourceAdapter adapter)
        {
            return EnsurePorts(adapter, SourcePortDetector.Detect());
        }

        public static IReadOnlyList<ISourcePortData> EnsurePorts(IDataSourceAdapter adapter, IEnumerable<DetectedSourcePort> detected)
        {
            if (adapter == null)
                throw new ArgumentNullException(nameof(adapter));

            var added = new List<ISourcePortData>();
            var existing = adapter.GetSourcePorts().ToList();
            foreach (var found in detected ?? Array.Empty<DetectedSourcePort>())
            {
                if (AlreadyConfigured(existing, found))
                    continue;
                var port = ToSourcePort(found);
                adapter.InsertSourcePort(port);
                existing.Add(port);
                added.Add(port);
            }

            EnsureDefaultSourcePort(adapter);
            return added;
        }

        public static ISourcePortData EnsureDefaultSourcePort(IDataSourceAdapter adapter)
        {
            if (adapter == null)
                return null;

            var ports = adapter.GetSourcePorts().ToList();
            if (ports.Count == 0)
                return null;

            var preferred = ports.FirstOrDefault(p => SourcePortLaunch.IsZDoomFamily(p.Executable))
                ?? ports.First();

            var config = adapter.GetConfiguration().FirstOrDefault(x => x.Name == ConfigType.DefaultSourcePort.ToString("g"));
            if (config != null)
            {
                bool missing = !int.TryParse(config.Value, out int id) || id <= 0 || adapter.GetSourcePort(id) == null;
                if (missing)
                {
                    config.Value = preferred.SourcePortID.ToString();
                    adapter.UpdateConfiguration(config);
                }
            }

            return preferred;
        }

        public static IIWadData EnsureDefaultIWad(IDataSourceAdapter adapter)
        {
            if (adapter == null)
                return null;
            var iwads = adapter.GetIWads().ToList();
            if (iwads.Count == 0)
                return null;

            var preferred = iwads.FirstOrDefault(x =>
                    !string.IsNullOrEmpty(x.FileName) &&
                    x.FileName.IndexOf("doom2", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? iwads.FirstOrDefault(x =>
                    !string.IsNullOrEmpty(x.FileName) &&
                    x.FileName.IndexOf("freedoom2", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? iwads.First();

            var config = adapter.GetConfiguration().FirstOrDefault(x => x.Name == ConfigType.DefaultIWad.ToString("g"));
            if (config != null)
            {
                bool missing = !int.TryParse(config.Value, out int id) || id <= 0 || adapter.GetIWadByIWadID(id) == null;
                if (missing)
                {
                    config.Value = preferred.IWadID.ToString();
                    adapter.UpdateConfiguration(config);
                }
            }

            return preferred;
        }
    }
}
