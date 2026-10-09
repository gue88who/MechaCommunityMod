; Build through build-installer.ps1; generated paths and hashes are in BuildRoot.
#ifndef BuildRoot
  #error Supply /DBuildRoot through build-installer.ps1
#endif
#include BuildRoot + "\build-values.iss"

[Setup]
AppId={{97180241-F17A-4EB8-94EA-98736583030E}
AppName=Mecha Community Mod
AppVersion={#ModVersion}
DefaultDirName={code:FindGameDirectory}
AppendDefaultDirName=no
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#BuildRoot}\dist
OutputBaseFilename=MechaCommunityMod-{#ModVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallFilesDir={app}\MechaCommunityMod\setup
UninstallDisplayName=Mecha Community Mod
LicenseFile={#BuildRoot}\payload\LICENSE.txt
InfoAfterFile={#BuildRoot}\payload\INSTALL.txt
CloseApplications=no
RestartApplications=no
DirExistsWarning=no
UsePreviousAppDir=yes
SetupLogging=yes

[Files]
; The loader is shared infrastructure: uninstalling Mecha Community Mod must not break other mods.
Source: "{#BuildRoot}\loader\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs uninsneveruninstall; Check: NeedsLoader
Source: "{#BuildRoot}\payload\MechaCommunityMod.BattleStatistics.dll"; DestDir: "{app}\BepInEx\plugins\MechaCommunityMod\BattleStatistics"; Flags: ignoreversion
Source: "{#BuildRoot}\payload\LICENSE.txt"; DestDir: "{app}\BepInEx\plugins\MechaCommunityMod\BattleStatistics"; Flags: ignoreversion
Source: "{#BuildRoot}\payload\INSTALL.txt"; DestDir: "{app}\BepInEx\plugins\MechaCommunityMod\BattleStatistics"; Flags: ignoreversion
Source: "{#BuildRoot}\launcher\*"; DestDir: "{app}\MechaCommunityMod\Launcher"; Flags: ignoreversion recursesubdirs
Source: "{#BuildRoot}\payload\default-settings.json"; DestDir: "{app}\MechaCommunityMod"; Flags: ignoreversion
Source: "{#BuildRoot}\payload\manifest.json"; DestDir: "{app}\MechaCommunityMod"; Flags: ignoreversion
Source: "{#BuildRoot}\payload\BepInEx-changelog.txt"; DestDir: "{app}\MechaCommunityMod"; Flags: ignoreversion
Source: "{#BuildRoot}\payload\licenses\*"; DestDir: "{app}\MechaCommunityMod\licenses"; Flags: ignoreversion
Source: "{#BuildRoot}\payload\2022.3.62.zip"; DestDir: "{app}\BepInEx\unity-libs"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{app}\Mecha Community Mod Launcher"; Filename: "{app}\MechaCommunityMod\Launcher\MechaCommunityMod.Launcher.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\MechaCommunityMod\Launcher\MechaCommunityMod.Launcher.exe"; Description: "Open Mecha Community Mod Launcher"; Flags: postinstall skipifsilent runasoriginaluser

[UninstallDelete]
Type: files; Name: "{app}\MechaCommunityMod\owns-loader.txt"
Type: files; Name: "{app}\MechaCommunityMod\console-original.txt"
Type: files; Name: "{app}\MechaCommunityMod\unity-source-original.txt"
Type: dirifempty; Name: "{app}\BepInEx\plugins\MechaCommunityMod\BattleStatistics"
Type: dirifempty; Name: "{app}\BepInEx\plugins\MechaCommunityMod"
Type: dirifempty; Name: "{app}\MechaCommunityMod"

[Code]
var
  InstallLoader: Boolean;
  FeaturesPage: TInputOptionWizardPage;
  LastFeaturesDirectory: String;

#include "UpdateCheck.iss"

function InitializeSetup: Boolean;
begin
  Result := True;
  if Pos('/UPDATECHECKSELFTEST', Uppercase(GetCmdTail)) > 0 then begin
    CheckUpdateRules;
    Result := False; { Exit before showing pages or changing installed files. }
  end;
end;

procedure InitializeWizard;
begin
  FeaturesPage := CreateInputOptionPage(wpSelectDir, 'Battle Statistics plugin',
    'Choose which features appear in Mechabellum.', 'You can change these choices later in the launcher.', False, False);
  FeaturesPage.Add('Live battle stats overlay (F8)');
  FeaturesPage.Add('Live individual unit stats');
  FeaturesPage.Add('Post-game stats');
  FeaturesPage.Values[0] := '{#DefaultLiveOverlay}' = 'true';
  FeaturesPage.Values[1] := '{#DefaultIndividualStats}' = 'true';
  FeaturesPage.Values[2] := '{#DefaultPostGameStats}' = 'true';
  InitializeUpdateCheck;
end;

procedure CurPageChanged(CurPageID: Integer);
var Config: String;
begin
  if (CurPageID = FeaturesPage.ID) and (LastFeaturesDirectory <> WizardDirValue) then begin
    LastFeaturesDirectory := WizardDirValue;
    Config := AddBackslash(WizardDirValue) + 'BepInEx\config\mecha.community.mod.battle-statistics.cfg';
    if not FileExists(Config) then Config := AddBackslash(WizardDirValue) + 'BepInEx\config\tools.replay.damage-overlay.cfg';
    FeaturesPage.Values[0] := CompareText(GetIniString('Features', 'LiveOverlay', '{#DefaultLiveOverlay}', Config), 'true') = 0;
    FeaturesPage.Values[1] := CompareText(GetIniString('Features', 'IndividualStats', '{#DefaultIndividualStats}', Config), 'true') = 0;
    FeaturesPage.Values[2] := CompareText(GetIniString('Features', 'PostGameStats', '{#DefaultPostGameStats}', Config), 'true') = 0;
  end;
end;

function NeedsLoader: Boolean;
begin
  Result := InstallLoader;
end;

function GameExists(const Directory: String): Boolean;
begin
  Result := FileExists(AddBackslash(Directory) + 'Mechabellum.exe') and
    FileExists(AddBackslash(Directory) + 'GameAssembly.dll') and
    DirExists(AddBackslash(Directory) + 'Mechabellum_Data');
end;

function QuotedValue(const Line: String): String;
var
  S: String;
  I: Integer;
begin
  Result := '';
  S := Trim(Line);
  if (Length(S) = 0) or (S[1] <> '"') then Exit;
  Delete(S, 1, 1);
  I := Pos('"', S);
  if I = 0 then Exit;
  Delete(S, 1, I);
  S := Trim(S);
  if (Length(S) = 0) or (S[1] <> '"') then Exit;
  Delete(S, 1, 1);
  I := Pos('"', S);
  if I = 0 then Exit;
  Result := Copy(S, 1, I - 1);
  StringChangeEx(Result, '\\', '\', True);
end;

function FindGameDirectory(Param: String): String;
var
  Steam, Candidate: String;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := ExpandConstant('{commonpf32}\Steam\steamapps\common\Mechabellum');
  if not RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', Steam) then
    if not RegQueryStringValue(HKLM32, 'Software\Valve\Steam', 'InstallPath', Steam) then Exit;
  Candidate := AddBackslash(Steam) + 'steamapps\common\Mechabellum';
  if GameExists(Candidate) then begin Result := Candidate; Exit; end;
  if not LoadStringsFromFile(AddBackslash(Steam) + 'steamapps\libraryfolders.vdf', Lines) then Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
    if Pos('"path"', Lines[I]) > 0 then begin
      Candidate := AddBackslash(QuotedValue(Lines[I])) + 'steamapps\common\Mechabellum';
      if GameExists(Candidate) then begin Result := Candidate; Exit; end;
    end;
end;

function ProcessCheck: String;
var
  Locator, Services, Processes: Variant;
begin
  Result := '';
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\cimv2');
    Processes := Services.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''Mechabellum.exe''');
    if Processes.Count > 0 then Result := 'Close Mechabellum before installing or uninstalling Mecha Community Mod.';
  except
    Result := 'Setup could not check whether Mechabellum is running. Please close the game and retry after Windows Management Instrumentation is available.';
  end;
end;

function FileMatches(const Directory, RelativePath, ExpectedHash: String): Boolean;
var
  Filename: String;
begin
  Filename := AddBackslash(Directory) + RelativePath;
  Result := False;
  if not FileExists(Filename) then Exit;
  try
    Result := CompareText(GetSHA256OfFile(Filename), ExpectedHash) = 0;
  except
    Result := False;
  end;
end;

function LoaderMatches(const Directory: String): Boolean;
begin
  Result := True;
  { Check the exact bundled core/runtime and proxy. Config is checked separately. }
  #include BuildRoot + "\loader-checks.iss"
end;

function ValidateDirectory(const Directory: String): String;
var
  Config: String;
begin
  Result := '';
  InstallLoader := False;
  if not GameExists(Directory) then begin
    Result := 'Select the Mechabellum folder containing Mechabellum.exe, GameAssembly.dll and Mechabellum_Data.';
    Exit;
  end;
  if DirExists(AddBackslash(Directory) + 'MelonLoader') then begin
    Result := 'MelonLoader was found here. Mecha Community Mod requires BepInEx; this installer does not support running both loaders together. No files have been changed.';
    Exit;
  end;
  if DirExists(AddBackslash(Directory) + 'BepInEx') then begin
    if not LoaderMatches(Directory) then begin
      Result := 'The existing BepInEx files do not match supported build 788 (Unity IL2CPP, Windows x64). Setup will not replace an existing loader. No files have been changed.';
      Exit;
    end;
    Config := AddBackslash(Directory) + 'doorstop_config.ini';
    if (CompareText(GetIniString('General', 'enabled', '', Config), 'true') <> 0) or
       (CompareText(GetIniString('General', 'target_assembly', '', Config), 'BepInEx\core\BepInEx.Unity.IL2CPP.dll') <> 0) or
       (CompareText(GetIniString('Il2Cpp', 'coreclr_path', '', Config), 'dotnet\coreclr.dll') <> 0) or
       (CompareText(GetIniString('Il2Cpp', 'corlib_dir', '', Config), 'dotnet') <> 0) then begin
      Result := 'The existing Doorstop configuration is disabled or uses a different loader/runtime path. Setup will leave it unchanged.';
      Exit;
    end;
  end else begin
    if FileExists(AddBackslash(Directory) + 'winhttp.dll') or
       FileExists(AddBackslash(Directory) + 'doorstop_config.ini') or
       FileExists(AddBackslash(Directory) + '.doorstop_version') or
       DirExists(AddBackslash(Directory) + 'dotnet') then begin
      Result := 'Existing loader or runtime files were found. Setup cannot safely install BepInEx over these files. No files have been changed.';
      Exit;
    end;
    InstallLoader := True;
  end;
  if FileExists(AddBackslash(Directory) + 'version.dll') then
    Result := 'An existing version.dll startup proxy was found. Its compatibility with BepInEx has not been verified. Setup will leave it unchanged.';
  Config := AddBackslash(Directory) + 'BepInEx\unity-libs\2022.3.62.zip';
  if FileExists(Config) and not FileMatches(Directory, 'BepInEx\unity-libs\2022.3.62.zip', '575E7D600F69DE8200CCF4DB700B3AE6252366C22E8C3434C860E428974518D1') then
    Result := 'The existing Unity reference archive does not match the bundled version. No files have been changed.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Config, Parameters: String; Code: Integer; I: Integer; Names: TArrayOfString;
begin
  if (CurStep = ssInstall) and (UpdateButton <> nil) then UpdateButton.Visible := False;
  if CurStep <> ssPostInstall then Exit;
  Config := ExpandConstant('{app}\BepInEx\config\mecha.community.mod.battle-statistics.cfg');
  ForceDirectories(ExtractFileDir(Config));
  SetArrayLength(Names, 3);
  Names[0] := 'LiveOverlay'; Names[1] := 'IndividualStats'; Names[2] := 'PostGameStats';
  for I := 0 to 2 do
    if FeaturesPage.Values[I] then SetIniString('Features', Names[I], 'true', Config)
    else SetIniString('Features', Names[I], 'false', Config);
  Parameters := '--initialize';
  if InstallLoader then Parameters := Parameters + ' --owns-loader';
  if not Exec(ExpandConstant('{app}\MechaCommunityMod\Launcher\MechaCommunityMod.Launcher.exe'), Parameters,
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    RaiseException('Could not initialize the launcher settings. Run the launcher to complete configuration.');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Parameters: String; Code: Integer;
begin
  if CurUninstallStep <> usUninstall then Exit;
  Parameters := '--uninstall-cleanup';
  if Pos('/REMOVESTATS', Uppercase(GetCmdTail)) > 0 then Parameters := Parameters + ' --remove-stats';
  if not Exec(ExpandConstant('{app}\MechaCommunityMod\Launcher\MechaCommunityMod.Launcher.exe'), Parameters,
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    RaiseException('Cleanup could not finish. Close the game and launcher, then run uninstall again.');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Problem: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then begin
    Problem := ValidateDirectory(WizardDirValue);
    Result := Problem = '';
    if not Result then MsgBox(Problem, mbError, MB_OK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := ProcessCheck;
  if Result = '' then Result := ValidateDirectory(WizardDirValue);
end;

function InitializeUninstall: Boolean;
var
  Problem: String;
begin
  Problem := ProcessCheck;
  Result := Problem = '';
  if not Result then MsgBox(Problem, mbError, MB_OK);
end;
