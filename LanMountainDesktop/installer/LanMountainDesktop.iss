#define MyAppName "LanMountainDesktop"
#define MyAppPublisher "LanMountainDesktop Team"
#define MyAppExeName "LanMountainDesktop.Launcher.exe"
#define MyAppId "{{5A058B0D-F95D-4A18-B9A0-93F843655DDB}"
#define MyAppRegistryId "{5A058B0D-F95D-4A18-B9A0-93F843655DDB}"

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

#ifndef MyAppSuffix
  #define MyAppSuffix ""
#endif

#ifndef IsSelfContained
  #define IsSelfContained "true"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=no
UsePreviousAppDir=no
ShowLanguageDialog=yes
UsePreviousLanguage=no
LanguageDetectionMethod=uilanguage
DefaultGroupName={cm:AppShortcutName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}-{#MyAppArch}{#MyAppSuffix}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Leave PrivilegesRequiredOverridesAllowed unset so users cannot downgrade
; installation mode via dialog or /ALLUSERS /CURRENTUSER command-line switches.
PrivilegesRequired=admin
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
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
Name: "chinesesimplified"; MessagesFile: "{#SourcePath}\ChineseSimplified.isl"

[CustomMessages]
english.StartupTaskDescription=Launch LanMountainDesktop when you sign in to Windows
chinesesimplified.StartupTaskDescription=登录 Windows 时启动 LanMountainDesktop
english.AppShortcutName=LanMountainDesktop
chinesesimplified.AppShortcutName=阑山桌面
english.WebView2MissingMessage=Microsoft Edge WebView2 Runtime is required for the browser component.
chinesesimplified.WebView2MissingMessage=浏览器组件需要 Microsoft Edge WebView2 Runtime。
english.WebView2MissingAction=Click "Yes" to open the official download page. Install it first, then run this installer again.
chinesesimplified.WebView2MissingAction=单击“是”打开官方下载页面。请先完成安装，然后重新运行此安装程序。
english.WebView2OpenFailedMessage=Unable to open the download page automatically.
chinesesimplified.WebView2OpenFailedMessage=无法自动打开下载页面。
english.WebView2OpenFailedAction=Please open this URL manually:
chinesesimplified.WebView2OpenFailedAction=请手动打开以下链接：
english.UpgradePageCaption=Upgrade Existing Installation
chinesesimplified.UpgradePageCaption=升级现有安装
english.UpgradePageDescription=LanMountainDesktop is already installed on this computer. Choose how to upgrade it.
chinesesimplified.UpgradePageDescription=此计算机上已安装 LanMountainDesktop。请选择升级方式。
english.UpgradeDetectedVersionLabel=Detected version:
chinesesimplified.UpgradeDetectedVersionLabel=检测到的版本：
english.UpgradeCurrentLocationLabel=Current location:
chinesesimplified.UpgradeCurrentLocationLabel=当前安装位置：
english.UpgradePageSubCaption=Choose "Upgrade existing installation" to reuse the current location, or choose "Change installation location and migrate installation" to move the app without leaving a duplicate copy behind.
chinesesimplified.UpgradePageSubCaption=选择“升级现有安装”可复用当前安装位置；选择“更改安装位置并迁移安装”可移动应用，同时避免留下重复安装。
english.UpgradeOptionInPlace=Upgrade existing installation
chinesesimplified.UpgradeOptionInPlace=升级现有安装
english.UpgradeOptionRelocate=Change installation location and migrate installation
chinesesimplified.UpgradeOptionRelocate=更改安装位置并迁移安装
english.UpgradeUnknownVersion=Unknown
chinesesimplified.UpgradeUnknownVersion=未知
english.UpgradeCleanupMissingUninstaller=Setup found an existing installation, but its uninstaller is unavailable. Please uninstall the current version manually and run this installer again.
chinesesimplified.UpgradeCleanupMissingUninstaller=安装程序发现了现有安装，但无法找到它的卸载程序。请先手动卸载当前版本，再重新运行此安装程序。
english.UpgradeCleanupFailedPrefix=Setup could not remove the existing installation automatically. Error code:
chinesesimplified.UpgradeCleanupFailedPrefix=安装程序无法自动移除现有安装。错误代码：
english.UpgradeCleanupFailedSuffix=Please close LanMountainDesktop, uninstall the current version manually, and then run this installer again.
chinesesimplified.UpgradeCleanupFailedSuffix=请关闭 LanMountain Desktop，手动卸载当前版本，然后重新运行此安装程序。
english.DotNetRuntimeMissingTitle=.NET Desktop Runtime Required
chinesesimplified.DotNetRuntimeMissingTitle=需要 .NET Desktop Runtime
english.DotNetRuntimeMissingMessage=This application requires .NET 10.0 Desktop Runtime to run.
chinesesimplified.DotNetRuntimeMissingMessage=此应用程序需要 .NET 10.0 Desktop Runtime 才能运行。
english.DotNetRuntimeMissingAction=Click "Yes" to open the official download page. Install it first, then run this installer again.
chinesesimplified.DotNetRuntimeMissingAction=单击"是"打开官方下载页面。请先完成安装，然后重新运行此安装程序。
english.DotNetRuntimeOpenFailedMessage=Unable to open the download page automatically.
chinesesimplified.DotNetRuntimeOpenFailedMessage=无法自动打开下载页面。
english.DotNetRuntimeOpenFailedAction=Please open this URL manually:
chinesesimplified.DotNetRuntimeOpenFailedAction=请手动打开以下链接：

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "{cm:StartupTaskDescription}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
Name: "{app}\log"; Permissions: users-modify

[InstallDelete]
Type: files; Name: "{app}\LanMontainDesktop.exe"
Type: files; Name: "{app}\LanMontainDesktop.dll"
Type: files; Name: "{app}\LanMontainDesktop.deps.json"
Type: files; Name: "{app}\LanMontainDesktop.runtimeconfig.json"
Type: files; Name: "{app}\LanMontainDesktop.pdb"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{cm:AppShortcutName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{cm:AppShortcutName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  UninstallRegSubkey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppRegistryId}_is1';
  WebView2RuntimeKeyPath = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2RuntimeDownloadUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  DotNetRuntimeDownloadUrl = 'https://dotnet.microsoft.com/download/dotnet/10.0';
  UpgradeChoiceInPlace = 0;
  UpgradeChoiceRelocate = 1;

var
  UpgradeModePage: TInputOptionWizardPage;
  ExistingInstallFound: Boolean;
  ExistingInstallPath: String;
  ExistingInstallVersion: String;
  ExistingUninstallCommand: String;
  ExistingInstallWas64Bit: Boolean;
  ExistingInstallIsPerUser: Boolean;
  ExistingInstallRemoved: Boolean;

function NormalizePathValue(const Value: String): String;
begin
  Result := RemoveBackslashUnlessRoot(Trim(Value));
end;

function CombinePath(const BasePath: String; const ChildName: String): String;
begin
  if BasePath = '' then
  begin
    Result := ChildName;
  end
  else
  begin
    Result := NormalizePathValue(BasePath) + '\' + ChildName;
  end;
end;

function SamePath(const LeftPath: String; const RightPath: String): Boolean;
begin
  Result := CompareText(NormalizePathValue(LeftPath), NormalizePathValue(RightPath)) = 0;
end;

function ExtractExecutableFromCommand(const CommandLine: String): String;
var
  CommandText: String;
  ClosingQuotePos: Integer;
  ExePos: Integer;
begin
  Result := '';
  CommandText := Trim(CommandLine);
  if CommandText = '' then
  begin
    exit;
  end;

  if CommandText[1] = '"' then
  begin
    Delete(CommandText, 1, 1);
    ClosingQuotePos := Pos('"', CommandText);
    if ClosingQuotePos > 0 then
    begin
      Result := Copy(CommandText, 1, ClosingQuotePos - 1);
    end
    else
    begin
      Result := CommandText;
    end;
  end
  else
  begin
    ExePos := Pos('.exe', LowerCase(CommandText));
    if ExePos > 0 then
    begin
      Result := Copy(CommandText, 1, ExePos + 3);
    end
    else
    begin
      Result := CommandText;
    end;
  end;

  Result := NormalizePathValue(RemoveQuotes(Result));
end;

function GetExistingExecutablePath(): String;
begin
  if ExistingInstallPath = '' then
  begin
    Result := '';
  end
  else
  begin
    Result := CombinePath(ExistingInstallPath, '{#MyAppExeName}');
  end;
end;

function GetDefaultInstallPath(): String;
begin
  Result := NormalizePathValue(ExpandConstant('{autopf}\{#MyAppName}'));
end;

function GetExistingInstallVersionText(): String;
begin
  Result := Trim(ExistingInstallVersion);
  if Result = '' then
  begin
    Result := CustomMessage('UpgradeUnknownVersion');
  end;
end;

procedure ShowUpgradeCleanupError(const MessageText: String);
begin
  Log(MessageText);
  if not WizardSilent then
  begin
    MsgBox(MessageText, mbError, MB_OK);
  end;
end;

function TryLoadExistingInstallation(
  const RootKey: Integer;
  const Is64BitView: Boolean;
  const IsPerUser: Boolean): Boolean;
var
  InstallLocation: String;
  AppPath: String;
  UninstallString: String;
  DisplayVersion: String;
  ResolvedPath: String;
begin
  Result := False;
  InstallLocation := '';
  AppPath := '';
  UninstallString := '';
  DisplayVersion := '';

  if not RegKeyExists(RootKey, UninstallRegSubkey) then
  begin
    exit;
  end;

  RegQueryStringValue(RootKey, UninstallRegSubkey, 'InstallLocation', InstallLocation);
  RegQueryStringValue(RootKey, UninstallRegSubkey, 'Inno Setup: App Path', AppPath);
  RegQueryStringValue(RootKey, UninstallRegSubkey, 'UninstallString', UninstallString);
  RegQueryStringValue(RootKey, UninstallRegSubkey, 'DisplayVersion', DisplayVersion);

  ResolvedPath := NormalizePathValue(InstallLocation);
  if ResolvedPath = '' then
  begin
    ResolvedPath := NormalizePathValue(AppPath);
  end;
  if (ResolvedPath = '') and (UninstallString <> '') then
  begin
    ResolvedPath := NormalizePathValue(ExtractFileDir(ExtractExecutableFromCommand(UninstallString)));
  end;

  if (ResolvedPath = '') or
     (not DirExists(ResolvedPath)) or
     (not FileExists(CombinePath(ResolvedPath, '{#MyAppExeName}'))) then
  begin
    exit;
  end;

  ExistingInstallFound := True;
  ExistingInstallPath := ResolvedPath;
  ExistingInstallVersion := Trim(DisplayVersion);
  ExistingUninstallCommand := Trim(UninstallString);
  ExistingInstallWas64Bit := Is64BitView;
  ExistingInstallIsPerUser := IsPerUser;
  Result := True;
end;

procedure DetectExistingInstallation;
begin
  ExistingInstallFound := False;
  ExistingInstallPath := '';
  ExistingInstallVersion := '';
  ExistingUninstallCommand := '';
  ExistingInstallWas64Bit := False;
  ExistingInstallIsPerUser := False;
  ExistingInstallRemoved := False;

  if IsWin64 then
  begin
    if TryLoadExistingInstallation(HKLM64, True, False) then
    begin
      exit;
    end;
    if TryLoadExistingInstallation(HKCU64, True, True) then
    begin
      exit;
    end;
  end;

  if TryLoadExistingInstallation(HKLM32, False, False) then
  begin
    exit;
  end;

  TryLoadExistingInstallation(HKCU32, False, True);
end;

function SelectedUpgradeChoice(): Integer;
begin
  if UpgradeModePage <> nil then
  begin
    Result := UpgradeModePage.SelectedValueIndex;
  end
  else
  begin
    Result := UpgradeChoiceInPlace;
  end;
end;

procedure ApplySelectedInstallDirectory;
var
  CurrentDir: String;
begin
  if not ExistingInstallFound then
  begin
    exit;
  end;

  if SelectedUpgradeChoice() = UpgradeChoiceInPlace then
  begin
    WizardForm.DirEdit.Text := ExistingInstallPath;
    exit;
  end;

  CurrentDir := NormalizePathValue(WizardDirValue);
  if (CurrentDir = '') or SamePath(CurrentDir, GetDefaultInstallPath()) then
  begin
    WizardForm.DirEdit.Text := ExistingInstallPath;
  end;
end;

function GetSelectedInstallPath(): String;
begin
  Result := NormalizePathValue(ExpandConstant('{app}'));
  if Result = '' then
  begin
    Result := NormalizePathValue(WizardDirValue);
  end;
  if Result = '' then
  begin
    Result := ExistingInstallPath;
  end;
end;

function ExistingInstallRequiresCleanup(): Boolean;
var
  TargetPath: String;
begin
  Result := False;
  if not ExistingInstallFound or ExistingInstallRemoved then
  begin
    exit;
  end;

  TargetPath := GetSelectedInstallPath();
  Result := ExistingInstallIsPerUser or
            (not SamePath(TargetPath, ExistingInstallPath)) or
            (ExistingInstallWas64Bit <> Is64BitInstallMode);
end;

function RemoveExistingInstallation(): Boolean;
var
  UninstallerPath: String;
  ResultCode: Integer;
begin
  Result := True;

  if not ExistingInstallRequiresCleanup() then
  begin
    exit;
  end;

  UninstallerPath := ExtractExecutableFromCommand(ExistingUninstallCommand);
  if (UninstallerPath = '') or (not FileExists(UninstallerPath)) then
  begin
    ShowUpgradeCleanupError(CustomMessage('UpgradeCleanupMissingUninstaller'));
    Result := False;
    exit;
  end;

  ResultCode := -1;
  if not Exec(
    UninstallerPath,
    '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART',
    ExtractFileDir(UninstallerPath),
    SW_SHOWNORMAL,
    ewWaitUntilTerminated,
    ResultCode) or (ResultCode <> 0) then
  begin
    ShowUpgradeCleanupError(
      CustomMessage('UpgradeCleanupFailedPrefix') + ' ' + IntToStr(ResultCode) + '. ' +
      CustomMessage('UpgradeCleanupFailedSuffix'));
    Result := False;
    exit;
  end;

  ExistingInstallRemoved := True;
end;

function IsWebView2RuntimeInstalled(): Boolean;
var
  VersionValue: String;
begin
  Result :=
    RegQueryStringValue(HKLM64, WebView2RuntimeKeyPath, 'pv', VersionValue) or
    RegQueryStringValue(HKLM32, WebView2RuntimeKeyPath, 'pv', VersionValue) or
    RegQueryStringValue(HKCU64, WebView2RuntimeKeyPath, 'pv', VersionValue) or
    RegQueryStringValue(HKCU32, WebView2RuntimeKeyPath, 'pv', VersionValue);
end;

// Checks whether a .NET 10.x shared framework is installed under the given
// base path by enumerating version sub-directories and looking for one that
// starts with '10.'.
function IsDotNet10RuntimePresent(const BasePath: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if not DirExists(BasePath) then
  begin
    exit;
  end;

  if FindFirst(BasePath + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
           (Length(FindRec.Name) >= 3) and
           (Copy(FindRec.Name, 1, 3) = '10.') then
        begin
          Result := True;
          exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Returns True when the .NET 10 Desktop Runtime (or the .NET 10 Core Runtime
// which is sufficient for Avalonia apps) is found on the system.
// We check both Microsoft.WindowsDesktop.App and Microsoft.NETCore.App because
// the runtimeconfig.json may reference either framework depending on the
// publish mode and the app only needs the one it actually references.
function IsDotNetDesktopRuntimeInstalled(): Boolean;
var
  BasePath: String;
begin
  Result := False;

  // Check 64-bit Program Files
  BasePath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if IsDotNet10RuntimePresent(BasePath) then
  begin
    Result := True;
    exit;
  end;

  BasePath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.NETCore.App');
  if IsDotNet10RuntimePresent(BasePath) then
  begin
    Result := True;
    exit;
  end;

  // Check 32-bit Program Files
  BasePath := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if IsDotNet10RuntimePresent(BasePath) then
  begin
    Result := True;
    exit;
  end;

  BasePath := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.NETCore.App');
  if IsDotNet10RuntimePresent(BasePath) then
  begin
    Result := True;
    exit;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
  IsSelfContainedBuild: Boolean;
begin
  IsSelfContainedBuild := ('{#IsSelfContained}' = 'true');

  if not IsSelfContainedBuild then
  begin
    if not IsDotNetDesktopRuntimeInstalled() then
    begin
      if MsgBox(
        CustomMessage('DotNetRuntimeMissingMessage') + #13#10#13#10 +
        CustomMessage('DotNetRuntimeMissingAction'),
        mbConfirmation,
        MB_YESNO) = IDYES then
      begin
        if not ShellExec('open', DotNetRuntimeDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode) then
        begin
          MsgBox(
            CustomMessage('DotNetRuntimeOpenFailedMessage') + #13#10 +
            CustomMessage('DotNetRuntimeOpenFailedAction') + #13#10 + DotNetRuntimeDownloadUrl,
            mbError,
            MB_OK);
        end;
      end;
      Result := False;
      exit;
    end;
  end;

  if IsWebView2RuntimeInstalled() then
  begin
    Result := True;
    exit;
  end;

  if MsgBox(
    CustomMessage('WebView2MissingMessage') + #13#10#13#10 +
    CustomMessage('WebView2MissingAction'),
    mbConfirmation,
    MB_YESNO) = IDYES then
  begin
    if not ShellExec('open', WebView2RuntimeDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode) then
    begin
      MsgBox(
        CustomMessage('WebView2OpenFailedMessage') + #13#10 +
        CustomMessage('WebView2OpenFailedAction') + #13#10 + WebView2RuntimeDownloadUrl,
        mbError,
        MB_OK);
    end;
  end;

  Result := False;
end;

procedure InitializeWizard;
var
  DetailsText: String;
begin
  DetectExistingInstallation;

  if not ExistingInstallFound then
  begin
    exit;
  end;

  DetailsText :=
    CustomMessage('UpgradeDetectedVersionLabel') + ' ' + GetExistingInstallVersionText() + #13#10 +
    CustomMessage('UpgradeCurrentLocationLabel') + ' ' + ExistingInstallPath + #13#10#13#10 +
    CustomMessage('UpgradePageSubCaption');

  UpgradeModePage := CreateInputOptionPage(
    wpWelcome,
    CustomMessage('UpgradePageCaption'),
    CustomMessage('UpgradePageDescription'),
    DetailsText,
    True,
    False);
  UpgradeModePage.Add(CustomMessage('UpgradeOptionInPlace'));
  UpgradeModePage.Add(CustomMessage('UpgradeOptionRelocate'));
  UpgradeModePage.SelectedValueIndex := UpgradeChoiceInPlace;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (UpgradeModePage <> nil) and (CurPageID = UpgradeModePage.ID) then
  begin
    ApplySelectedInstallDirectory;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if (UpgradeModePage <> nil) and (PageID = UpgradeModePage.ID) then
  begin
    Result := not ExistingInstallFound;
    exit;
  end;

  if PageID = wpSelectDir then
  begin
    Result := ExistingInstallFound and (SelectedUpgradeChoice() = UpgradeChoiceInPlace);
  end;
end;

procedure RegisterExtraCloseApplicationsResources;
var
  ExistingExecutablePath: String;
begin
  if not ExistingInstallFound then
  begin
    exit;
  end;

  ExistingExecutablePath := GetExistingExecutablePath();
  if (ExistingExecutablePath <> '') and FileExists(ExistingExecutablePath) then
  begin
    RegisterExtraCloseApplicationsResource(False, ExistingExecutablePath);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  LauncherPath: String;
  AppDirPath: String;
begin
  if CurStep = ssInstall then
  begin
    if not RemoveExistingInstallation() then
    begin
      Abort;
    end;
  end;
  
  if CurStep = ssPostInstall then
  begin
    // 验证 Launcher 是否存在
    LauncherPath := ExpandConstant('{app}\{#MyAppExeName}');
    if not FileExists(LauncherPath) then
    begin
      MsgBox('安装验证失败: Launcher 可执行文件不存在。' + #13#10 + 
             '预期路径: ' + LauncherPath + #13#10 + #13#10 +
             '请联系开发者报告此问题。', mbError, MB_OK);
      Abort;
    end;
    
    // 验证至少存在一个 app-* 目录
    AppDirPath := ExpandConstant('{app}\app-{#MyAppVersion}');
    if not DirExists(AppDirPath) then
    begin
      MsgBox('安装验证失败: 应用版本目录不存在。' + #13#10 + 
             '预期路径: ' + AppDirPath + #13#10 + #13#10 +
             '请联系开发者报告此问题。', mbError, MB_OK);
      Abort;
    end;
  end;
end;
