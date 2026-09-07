; Inno Setup script for Grev Home.
;
; This does not build the app - run build-installer.ps1 first (or run the two
; commands it contains by hand). It only packages whatever is already sitting
; in publish\ into one downloadable Setup .exe.
;
; Compile with Inno Setup's ISCC.exe, e.g.:
;   ISCC.exe installer\GrevHome.iss

#define MyAppName "Grev Home"
#define MyAppVersion "0.13"
#define MyAppPublisher "Grev Home"
#define MyAppExeName "GrevHome.exe"
#define MyPublishDir "..\publish"

[Setup]
AppId={{EA4D6CAE-5909-4557-9BC2-0C9B89151999}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Grev Home already enforces one running instance itself via this named mutex
; (see App.xaml.cs). Reusing it here means Setup asks the user to close a
; running Grev Home before it overwrites its files, instead of failing partway.
AppMutex=Local\GrevHome.Shell.Instance
OutputDir=..\dist
OutputBaseFilename=GrevHomeSetup
SetupIconFile=..\src\GrevHome\Assets\Brand\GrevHome.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; A per-machine Program Files install (the default, admin-elevated) matches
; the rest of Grev Home's design: Games/BIOS/profile data is already shared
; machine-wide under C:\GrevHome rather than per-user, so the app itself
; should be too.
ArchitecturesInstallIn64BitMode=x64compatible
; Uninstall only ever removes what Setup put under {app}. Profiles, saves,
; and the Games/BIOS folders chosen during first-run setup all live outside
; {app} (under C:\GrevHome, or wherever the user pointed the wizard) and are
; never touched by install or uninstall.
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
