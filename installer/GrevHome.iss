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
#define MyAppVersion "0.14.7"
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
; C:\GrevCo is the shared home for Grev software. Grev Home's binaries and
; Grev-owned data now both live under this same GrevHome root by default.
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
; Installing outside Program Files does not require elevation, and Grev Home's
; default writable root is the same C:\GrevCo\GrevHome folder.
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
; Setup removes only files it installed. Runtime-created Profiles, Data, Games,
; BIOS, Packages and other Grev Home content under {app} are not installer-owned
; files and are deliberately preserved unless the user removes them separately.
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
const
  WebViewClient = 'Software\Microsoft\EdgeUpdate\Clients\{F1E7E5A1-5B7F-4A69-BE3B-22BBE6D17F7B}';

var
  UsagePage: TInputOptionWizardPage;
  ConsolePage: TInputOptionWizardPage;
  GamesPage: TInputOptionWizardPage;
  GamesFolderPage: TInputDirWizardPage;
  BiosPage: TInputOptionWizardPage;
  BiosFolderPage: TInputDirWizardPage;

function EmulatorSetupSelected(): Boolean;
begin
  Result := UsagePage.Values[0];
end;

function AnyConsoleSelected(): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 0 to ConsolePage.CheckListBox.Items.Count - 1 do
    if ConsolePage.Values[Index] then
    begin
      Result := True;
      Exit;
    end;
end;

procedure InitializeWizard();
begin
  UsagePage := CreateInputOptionPage(wpWelcome,
    'How will you use Grev Home?',
    'Choose whether this PC needs emulator setup.',
    'Grev Home will use this to prepare the right folders and first-launch steps.',
    True, False);
  UsagePage.Add('Set up emulators and console games');
  UsagePage.Add('PC games and apps only');
  UsagePage.SelectedValueIndex := 0;

  ConsolePage := CreateInputOptionPage(UsagePage.ID,
    'Which consoles will you use?',
    'Select every console you want Grev Home to prepare for.',
    'You can change or add systems later inside Grev Home.',
    False, False);
  ConsolePage.Add('Nintendo - Nintendo DS');
  ConsolePage.Add('Nintendo - Nintendo Switch');
  ConsolePage.Add('Nintendo - Nintendo 3DS');
  ConsolePage.Add('Microsoft - Original Xbox');
  ConsolePage.Add('Sony - PlayStation 2');

  GamesPage := CreateInputOptionPage(ConsolePage.ID,
    'Do you already have a Games folder?',
    'Grev Home can use an existing library or create its standard folder.',
    'The standard option is C:\GrevCo\GrevHome\Games.',
    True, False);
  GamesPage.Add('Yes - use my existing Games folder');
  GamesPage.Add('No - create the standard Grev Home Games folder');
  GamesPage.SelectedValueIndex := 1;

  GamesFolderPage := CreateInputDirPage(GamesPage.ID,
    'Choose the Games folder',
    'Select your existing folder or keep the standard Grev Home location.',
    'Grev Home will use this as the default location for scanning and emulator configuration.',
    False, '');
  GamesFolderPage.Add(ExpandConstant('{app}\Games'));

  BiosPage := CreateInputOptionPage(GamesFolderPage.ID,
    'Do you already have a BIOS folder?',
    'Selected console emulators may require BIOS or firmware files that you legally provide.',
    'Grev Home never downloads BIOS files. The standard folder is C:\GrevCo\GrevHome\bios.',
    True, False);
  BiosPage.Add('Yes - use my existing BIOS folder');
  BiosPage.Add('No - create the standard Grev Home BIOS folder');
  BiosPage.SelectedValueIndex := 1;

  BiosFolderPage := CreateInputDirPage(BiosPage.ID,
    'Choose the BIOS folder',
    'Select your existing folder or keep the standard Grev Home location.',
    'This shared location will be supplied to supported emulators during setup.',
    False, '');
  BiosFolderPage.Add(ExpandConstant('{app}\bios'));
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result :=
    ((PageID = ConsolePage.ID) and not EmulatorSetupSelected()) or
    (((PageID = GamesPage.ID) or (PageID = GamesFolderPage.ID)) and
      not EmulatorSetupSelected()) or
    (((PageID = BiosPage.ID) or (PageID = BiosFolderPage.ID)) and
      (not EmulatorSetupSelected() or not AnyConsoleSelected()));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = ConsolePage.ID) and EmulatorSetupSelected() and not AnyConsoleSelected() then
  begin
    MsgBox('Select at least one console, or go back and choose PC games and apps only.', mbInformation, MB_OK);
    Result := False;
  end;
end;

function SelectedConsoleList(): String;
var
  Index: Integer;
begin
  Result := '';
  for Index := 0 to ConsolePage.CheckListBox.Items.Count - 1 do
    if ConsolePage.Values[Index] then
    begin
      if Result <> '' then Result := Result + '|';
      Result := Result + ConsolePage.CheckListBox.Items[Index];
    end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  PreferencesFile: String;
  BiosFolder: String;
begin
  if CurStep <> ssPostInstall then Exit;
  ForceDirectories(GamesFolderPage.Values[0]);
  if EmulatorSetupSelected() and AnyConsoleSelected() then
  begin
    BiosFolder := BiosFolderPage.Values[0];
    ForceDirectories(BiosFolder);
  end
  else
    BiosFolder := ExpandConstant('{app}\bios');

  ForceDirectories(ExpandConstant('{app}\Data'));
  PreferencesFile := ExpandConstant('{app}\Data\installer-first-run.ini');
  SetIniString('Setup', 'GamesRoot', GamesFolderPage.Values[0], PreferencesFile);
  SetIniString('Setup', 'BiosRoot', BiosFolder, PreferencesFile);
  SetIniString('Setup', 'EmulatorSetup', BoolToStr(EmulatorSetupSelected(), True), PreferencesFile);
  SetIniString('Setup', 'Consoles', SelectedConsoleList(), PreferencesFile);
end;

function HasWebViewVersion(RootKey: Integer; SubKey: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, SubKey, 'pv', Version) and
            (Version <> '') and (Version <> '0.0.0.0');
end;

function IsWebView2Installed(): Boolean;
begin
  Result := HasWebViewVersion(HKCU, WebViewClient) or
            HasWebViewVersion(HKLM, WebViewClient) or
            HasWebViewVersion(HKLM32, WebViewClient) or
            HasWebViewVersion(HKLM64, WebViewClient);
end;
