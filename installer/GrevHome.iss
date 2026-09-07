; Inno Setup script for Grev Home.
;
; This does not build the app - run build-installer.ps1 first (or run the two
; commands it contains by hand). It only packages whatever is already sitting
; in publish\ into one downloadable Setup .exe.
;
; Compile with Inno Setup's ISCC.exe, e.g.:
;   ISCC.exe installer\GrevHome.iss

#define MyAppName "Grev Home"
#define MyAppDirName "GrevHome"
#define MyAppVersion "0.13"
#define MyAppPublisher "Grev Home"
#define MyAppExeName "GrevHome.exe"
#ifndef MyPublishDir
#define MyPublishDir "..\publish"
#endif
#ifndef MyWebViewBootstrapper
#define MyWebViewBootstrapper "MicrosoftEdgeWebview2Setup.exe"
#endif

[Setup]
AppId={{EA4D6CAE-5909-4557-9BC2-0C9B89151999}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; C:\GrevCo is a shared home for every piece of Grev software, with each app
; in its own subfolder - GrevHome here, room for others alongside it later.
DefaultDirName=C:\GrevCo\{#MyAppDirName}
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
; Installing outside Program Files no longer needs admin elevation - and the
; app itself already creates C:\GrevHome (its data root) the same way, with
; no elevation, on first run. Matching that here means no UAC prompt.
PrivilegesRequired=lowest
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
Source: "{#MyWebViewBootstrapper}"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing the Microsoft WebView2 browser runtime…"; Flags: waituntilterminated runhidden; Check: not IsWebView2Installed
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function HasWebViewVersion(RootKey: Integer; SubKey: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, SubKey, 'pv', Version) and
            (Version <> '') and (Version <> '0.0.0.0');
end;

function IsWebView2Installed: Boolean;
const
  WebViewClient = 'Software\Microsoft\EdgeUpdate\Clients\{F1E7E5A1-5B7F-4A69-BE3B-22BBE6D17F7B}';
begin
  Result := HasWebViewVersion(HKCU, WebViewClient) or
            HasWebViewVersion(HKLM, WebViewClient) or
            HasWebViewVersion(HKLM32, WebViewClient) or
            HasWebViewVersion(HKLM64, WebViewClient);
end;
