; The Windows installer, built by the release workflow with Inno Setup.
;
; What it does that the portable executables cannot: put the reader somewhere
; Windows knows about, teach the shell what a .koma file is, and put the
; command line on the PATH so that `koma convert` works from any prompt.
;
; What it deliberately does not do: run at startup, install a service, phone
; anywhere, or write outside its own folder and the registry keys below. The
; library stays in %APPDATA%\KOMA and survives an uninstall, because a reading
; position is the reader's and not the installer's.

#define Name "KOMA"
#define Publisher "arthur-lagenebre"
#define Site "https://github.com/arthur-lagenebre/koma-desktop"

; Passed in by the workflow: /DVersion=0.2.0
#ifndef Version
  #define Version "0.0.0"
#endif

[Setup]
AppId={{8F0A4F4C-6A45-4D0F-9C27-4F1B3C0E9A21}
AppName={#Name}
AppVersion={#Version}
AppPublisher={#Publisher}
AppPublisherURL={#Site}
AppSupportURL={#Site}/issues
AppUpdatesURL={#Site}/releases

; Per user, so that no administrator is needed: an unsigned installer asking
; for elevation asks for more trust than it has earned.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#Name}
DefaultGroupName={#Name}
DisableProgramGroupPage=yes
AllowNoIcons=yes

OutputBaseFilename=koma-setup-{#Version}-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Koma.Desktop\Assets\koma.ico
UninstallDisplayIcon={app}\Koma.Desktop.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Tell Windows that the environment and the file associations changed, so that
; a terminal opened after the install finds koma and the shell refreshes its
; icons. Without these, both wait for the next sign-in.
ChangesEnvironment=yes
ChangesAssociations=yes
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: "association"; Description: "Open .koma files with KOMA"
Name: "path"; Description: "Add the koma command line to PATH"

[Files]
Source: "Koma.Desktop.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "koma.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#Name}"; Filename: "{app}\Koma.Desktop.exe"
Name: "{autodesktop}\{#Name}"; Filename: "{app}\Koma.Desktop.exe"; Tasks: desktopicon

[Registry]
; §3 gives the media type; the shell only needs to know which program opens
; the extension and what to call it.
Root: HKCU; Subkey: "Software\Classes\.koma"; ValueType: string; ValueName: ""; ValueData: "KOMA.Publication"; Flags: uninsdeletevalue; Tasks: association
Root: HKCU; Subkey: "Software\Classes\.koma"; ValueType: string; ValueName: "Content Type"; ValueData: "application/vnd.koma+zip"; Flags: uninsdeletevalue; Tasks: association
Root: HKCU; Subkey: "Software\Classes\KOMA.Publication"; ValueType: string; ValueName: ""; ValueData: "KOMA publication"; Flags: uninsdeletekey; Tasks: association
Root: HKCU; Subkey: "Software\Classes\KOMA.Publication\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Koma.Desktop.exe,0"; Tasks: association
Root: HKCU; Subkey: "Software\Classes\KOMA.Publication\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Koma.Desktop.exe"" ""%1"""; Tasks: association

[Run]
Filename: "{app}\Koma.Desktop.exe"; Description: "{cm:LaunchProgram,{#Name}}"; Flags: nowait postinstall skipifsilent

[Code]
{ The PATH entry, added and removed by hand: Inno has no task for it, and a
  half-removed PATH is worse than none. Per user, as the install is. }

const
  Environment = 'Environment';

function PathHas(const Path, Folder: string): Boolean;
begin
  Result := Pos(';' + Uppercase(Folder) + ';', ';' + Uppercase(Path) + ';') > 0;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Path: string;
begin
  if (CurStep <> ssPostInstall) or not WizardIsTaskSelected('path') then
    Exit;

  if not RegQueryStringValue(HKCU, Environment, 'Path', Path) then
    Path := '';

  if PathHas(Path, ExpandConstant('{app}')) then
    Exit;

  if (Path <> '') and (Copy(Path, Length(Path), 1) <> ';') then
    Path := Path + ';';

  RegWriteExpandStringValue(HKCU, Environment, 'Path', Path + ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Path, Folder: string;
  At: Integer;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  if not RegQueryStringValue(HKCU, Environment, 'Path', Path) then
    Exit;

  Folder := ExpandConstant('{app}');
  At := Pos(Uppercase(Folder), Uppercase(Path));

  if At = 0 then
    Exit;

  Delete(Path, At, Length(Folder));

  { The separator that held it, wherever it was. }
  if (At > 1) and (Copy(Path, At - 1, 1) = ';') then
    Delete(Path, At - 1, 1)
  else if Copy(Path, At, 1) = ';' then
    Delete(Path, At, 1);

  RegWriteExpandStringValue(HKCU, Environment, 'Path', Path);
end;
