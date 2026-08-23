using DoomLauncher;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DoomLauncher.Linux
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dir))
                    Directory.SetCurrentDirectory(dir);

                var application = Adw.Application.New("com.goshapps.DoomLauncher", Gio.ApplicationFlags.HandlesOpen);
                LaunchArgs launchArgs = ParseLaunchArgs(args);
                MainWindow window = null;

                application.OnActivate += (sender, eventArgs) =>
                {
                    if (window != null)
                    {
                        window.Present();
                        return;
                    }

                    if (!AppBootstrap.Init(out string error))
                    {
                        GtkUtil.Alert(null, "Doom Launcher", error ?? "Failed to initialize.");
                        return;
                    }

                    AppBootstrap.CreateLinuxDesktopEntry();
                    GtkUtil.ApplyColorTheme(DataCache.Instance.AppConfiguration.ColorTheme);
                    window = new MainWindow((Adw.Application)sender, launchArgs);
                    window.Present();
                };

                application.OnShutdown += (sender, eventArgs) =>
                {
                    window?.SaveWindowConfig();
                };

                return application.RunWithSynchronizationContext(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        public static LaunchArgs ParseLaunchArgs(string[] args)
        {
            var launchArgs = new LaunchArgs();
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "--gapplication-service")
                    continue;
                if (!arg.StartsWith("-"))
                {
                    launchArgs.LaunchFileName = arg;
                    break;
                }

                string key = arg.TrimStart('-');
                if (key.Equals(nameof(LaunchArgs.AutoClose), StringComparison.OrdinalIgnoreCase))
                {
                    launchArgs.AutoClose = true;
                    continue;
                }

                if (key.Equals(nameof(LaunchArgs.LaunchGameFileID), StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i < args.Length && int.TryParse(args[i], out int id))
                        launchArgs.LaunchGameFileID = id;
                }
            }

            return launchArgs;
        }
    }
}
