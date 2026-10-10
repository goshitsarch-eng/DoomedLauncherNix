; ISCC /DAppVersion=... /DAppNumericVersion=... /DSourceDir=... /DOutputDir=...
; Invoked by scripts/package.py after the native bundle has been checked.
[Setup]
AppId=com.goshapps.DoomLauncher
AppName=Doom Launcher
AppVersion={#AppVersion}
VersionInfoVersion={#AppNumericVersion}
AppPublisher=Doom Launcher contributors
AppPublisherURL=https://github.com/goshitsarch-eng/DoomedLauncherNix
DefaultDirName={localappdata}\Programs\Doom Launcher
DefaultGroupName=Doom Launcher
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#PackageName}-setup
SetupIconFile=..\..\windows\runner\resources\app_icon.ico
UninstallDisplayIcon={app}\doomedlauncher.exe
LicenseFile=..\..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
#if TargetArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Doom Launcher"; Filename: "{app}\doomedlauncher.exe"
Name: "{autodesktop}\Doom Launcher"; Filename: "{app}\doomedlauncher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\doomedlauncher.exe"; Description: "Open Doom Launcher"; Flags: nowait postinstall skipifsilent

[Registry]
; Register Open With without changing any existing default file associations.
Root: HKCU; Subkey: "Software\Classes\Applications\doomedlauncher.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Doom Launcher"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\doomedlauncher.exe\shell\open\command"; ValueType: string; ValueData: """{app}\doomedlauncher.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\Applications\doomedlauncher.exe\SupportedTypes"; ValueType: string; ValueName: ".wad"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\doomedlauncher.exe\SupportedTypes"; ValueType: string; ValueName: ".pk3"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\doomedlauncher.exe\SupportedTypes"; ValueType: string; ValueName: ".deh"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\Applications\doomedlauncher.exe\SupportedTypes"; ValueType: string; ValueName: ".bex"; ValueData: ""
