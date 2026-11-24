#ifndef AppVersion
  #error AppVersion must be supplied by the build script
#endif
#ifndef PublishRoot
  #error PublishRoot must be supplied by the build script
#endif
#ifndef InstallerOutput
  #error InstallerOutput must be supplied by the build script
#endif

[Setup]
AppId={{D4058642-3B35-479C-8F7F-E22A26918A47}
AppName=Dockerino Lamborgino
AppVersion={#AppVersion}
AppPublisher=AlvaroGReg
AppPublisherURL=https://github.com/AlvaroGReg/dockerino-lamborgino
DefaultDirName={localappdata}\Programs\DockerinoLamborgino
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#InstallerOutput}
OutputBaseFilename=DockerinoLamborgino-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ChangesEnvironment=yes
CloseApplications=no
RestartApplications=no
SetupIconFile=..\assets\dockerino-lamborgino-icon.ico

[Files]
Source: "{#PublishRoot}\app\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#PublishRoot}\cli\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "dockerino.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishRoot}\licenses\*"; DestDir: "{app}\licenses"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Dockerino Lamborgino"; Filename: "{app}\DockerinoLamborgino.App.exe"

[Code]
const
  EnvironmentKey = 'Environment';
  SettingsKey = 'Software\DockerinoLamborgino\Installer';

function AppRunning: Boolean;
var
  Locator, Services, Processes: Variant;
begin
  { Fail closed if process enumeration is unavailable. }
  Result := True;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('.', 'root\cimv2');
    Processes := Services.ExecQuery(
      'SELECT ProcessId FROM Win32_Process WHERE Name = ''DockerinoLamborgino.App.exe''');
    Result := Processes.Count > 0;
  except
    Log('Could not determine whether Dockerino Lamborgino is running.');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if AppRunning then
    Result := 'Close Dockerino Lamborgino before installing or upgrading. ' +
      'Closing the app stops its managed terminal sessions. Retry afterwards.';
end;

function InitializeUninstall: Boolean;
begin
  Result := not AppRunning;
  if not Result then begin
    Log('Close Dockerino Lamborgino before uninstalling.');
    if not UninstallSilent then
      MsgBox('Close Dockerino Lamborgino before uninstalling. Closing the app stops its managed terminal sessions.',
        mbError, MB_OK);
  end;
end;

function ContainsPath(const Paths, Entry: String): Boolean;
var
  Remaining, Item: String;
  Separator: Integer;
begin
  Result := False;
  Remaining := Paths;
  while Remaining <> '' do begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then begin
      Item := Remaining;
      Remaining := '';
    end else begin
      Item := Copy(Remaining, 1, Separator - 1);
      Delete(Remaining, 1, Separator);
    end;
    if CompareText(Trim(Item), Entry) = 0 then begin
      Result := True;
      Exit;
    end;
  end;
end;

function RemovePath(const Paths, Entry: String): String;
var
  Remaining, Item: String;
  Separator: Integer;
  First: Boolean;
begin
  Result := '';
  Remaining := Paths;
  First := True;
  while Remaining <> '' do begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then begin
      Item := Remaining;
      Remaining := '';
    end else begin
      Item := Copy(Remaining, 1, Separator - 1);
      Delete(Remaining, 1, Separator);
    end;
    if CompareText(Trim(Item), Entry) <> 0 then begin
      if not First then Result := Result + ';';
      Result := Result + Item;
      First := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Paths, Entry: String;
begin
  if CurStep <> ssPostInstall then Exit;
  Entry := ExpandConstant('{app}');
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
    Paths := '';
  if not ContainsPath(Paths, Entry) then begin
    if Paths <> '' then Paths := Paths + ';';
    if not RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths + Entry) then
      RaiseException('Could not add Dockerino to the user PATH.');
    if not RegWriteStringValue(HKCU, SettingsKey, 'AddedPath', Entry) then
      RaiseException('Could not record the Dockerino PATH entry.');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Paths, Entry: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  if RegQueryStringValue(HKCU, SettingsKey, 'AddedPath', Entry) and
     RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then begin
    if RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', RemovePath(Paths, Entry)) then
      RegDeleteValue(HKCU, SettingsKey, 'AddedPath')
    else
      Log('Could not remove the Dockerino entry from the user PATH.');
  end;
end;
