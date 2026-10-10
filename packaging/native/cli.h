#ifndef DOOM_LAUNCHER_CLI_H_
#define DOOM_LAUNCHER_CLI_H_

#include <cstdio>
#include <string>
#include <vector>
#ifdef _WIN32
#include <windows.h>
#endif

// Handle terminal-only requests before initializing a display or Flutter.
inline bool HandleDoomLauncherCli(const std::vector<std::string>& arguments) {
  bool help = false;
  bool version = false;
  for (const auto& argument : arguments) {
    version = version || argument == "--version" || argument == "-v";
    help = help || argument == "--help" || argument == "-h";
  }
  if (!help && !version) return false;
  const std::string message = version
      ? "Doom Launcher " DOOM_APP_VERSION "\n"
      : "Doom Launcher\nUsage: doomedlauncher [--help] [--version] [FILE ...]\n\n"
        "Import positional files into your library and open the launcher.\n";
#ifdef _WIN32
  const HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);
  if (output != nullptr && output != INVALID_HANDLE_VALUE) {
    DWORD written = 0;
    WriteFile(output, message.data(), static_cast<DWORD>(message.size()), &written, nullptr);
  }
#else
  std::fputs(message.c_str(), stdout);
#endif
  return true;
}

#endif
