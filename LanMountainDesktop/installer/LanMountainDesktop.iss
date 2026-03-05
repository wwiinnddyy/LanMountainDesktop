#define MyAppName "LanMountainDesktop"
#define MyAppPublisher "LanMountainDesktop Team"
#define MyAppExeName "LanMountainDesktop.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

#ifndef MyOutputDir
  #define MyOutputDir "..\artifacts\installer"
#endif

#ifndef MyAppArch
  #define MyAppArch "x64"
#endif

[Setup]
AppId={{5A058B0D-F95D-4A18-B9A0-93F843655DDB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}-{#MyAppArch}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
DisableProgramGroupPage=yes

#if MyAppArch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
#if MyAppArch == "x86"
ArchitecturesAllowed=x86compatible
#endif
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Launch LanMountainDesktop when you sign in to Windows"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  WebView2RuntimeKeyPath = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2RuntimeDownloadUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

function IsWebView2RuntimeInstalled(): Boolean;
var
  VersionValue: string;
begin
  Result :=
    RegQueryStringValue(HKLM64, WebView2RuntimeKeyPath, 'pv', VersionValue) or
    RegQueryStringValue(HKLM32, WebView2RuntimeKeyPath, 'pv', VersionValue) or
    RegQueryStringValue(HKCU64, WebView2RuntimeKeyPath, 'pv', VersionValue) or
    RegQueryStringValue(HKCU32, WebView2RuntimeKeyPath, 'pv', VersionValue);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  if IsWebView2RuntimeInstalled() then
  begin
    Result := True;
    exit;
  end;

  if MsgBox(
    'Microsoft Edge WebView2 Runtime is required for the browser component.'#13#10#13#10 +
    'Click "Yes" to open the official download page. Install it first, then run this installer again.',
    mbConfirmation,
    MB_YESNO) = IDYES then
  begin
    if not ShellExec('open', WebView2RuntimeDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode) then
    begin
      MsgBox(
        'Unable to open the download page automatically.'#13#10 +
        'Please open this URL manually:'#13#10 + WebView2RuntimeDownloadUrl,
        mbError,
        MB_OK);
    end;
  end;

  Result := False;
end;
