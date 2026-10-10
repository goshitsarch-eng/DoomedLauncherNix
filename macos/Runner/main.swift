import Cocoa

// Keep --help and --version usable without launching an NSApplication/window.
let arguments = CommandLine.arguments.dropFirst()
if arguments.contains("--version") || arguments.contains("-v") {
  let version = Bundle.main.object(forInfoDictionaryKey: "DoomLauncherVersion") as? String ?? "unknown"
  print("Doom Launcher \(version)")
} else if arguments.contains("--help") || arguments.contains("-h") {
  print("Doom Launcher\nUsage: doomedlauncher [--help] [--version] [FILE ...]\n\nImport positional files into your library and open the launcher.")
} else {
  _ = NSApplicationMain(CommandLine.argc, CommandLine.unsafeArgv)
}
