; One Windows wizard owns installation, maintenance, and removal.
[Setup]
AppId=GrasshopperFilePreview.Bundle
SetupMutex=Local\GrasshopperFilePreview.Installer
AppName=Grasshopper Thumbnails & Preview
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
AppPublisher=Grasshopper Thumbnails & Preview contributors
AppPublisherURL=https://github.com/mahdiyargit/grasshopper-thumbnails
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=no
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
DefaultDirName={code:GetDefaultInstallDir}
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableReadyPage=no
DisableFinishedPage=no
UninstallDisplayName=Grasshopper Thumbnails & Preview
UninstallDisplayIcon={uninstallexe}
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
OutputDir={#OutputDirectory}
OutputBaseFilename=GrasshopperThumbnails-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Messages]
PrivilegesRequiredOverrideInstruction=Choose who can use this app
PrivilegesRequiredOverrideCurrentUser=Only for &me
PrivilegesRequiredOverrideCurrentUserRecommended=Only for &me (recommended)
PrivilegesRequiredOverrideAllUsers=&All users
PrivilegesRequiredOverrideAllUsersRecommended=&All users (recommended)
WelcomeLabel2=This will install thumbnails and previews for Grasshopper .gh and .ghx files in Windows Explorer.%n%nRhino is not required. Setup will use the installation scope you selected.
FinishedLabelNoIcons=Grasshopper Thumbnails & Preview has been installed.%n%nReopen any Explorer folders that were already open. Use Large icons for thumbnails and Alt+P for the preview pane.

[Files]
; The worker stages verified runtime files without overwriting a DLL loaded by Explorer.
Source: "{#BuildDirectory}\GrasshopperFilePreview.dll"; Flags: dontcopy
Source: "{#BuildDirectory}\GrasshopperFilePreview.Setup.exe"; Flags: dontcopy
Source: "{#BuildDirectory}\GrasshopperFilePreview.Setup.exe.config"; Flags: dontcopy
Source: "{#BuildDirectory}\GrasshopperFilePreview.Diagnostics.exe"; Flags: dontcopy
Source: "{#BuildDirectory}\GrasshopperFilePreview.Diagnostics.exe.config"; Flags: dontcopy
Source: "{#BuildDirectory}\Grasshopper.ico"; Flags: dontcopy
Source: "{#BuildDirectory}\GrasshopperXml.ico"; Flags: dontcopy
Source: "{#BuildDirectory}\LICENSE"; Flags: dontcopy
Source: "{#BuildDirectory}\THIRD_PARTY_NOTICES.md"; Flags: dontcopy
Source: "{#BuildDirectory}\README.md"; DestDir: "{app}\versions\{#PayloadDirectory}"; Flags: uninsrestartdelete

[UninstallDelete]
; Explicit ownership: never recursively delete the user's installation directory.
Type: files; Name: "{app}\versions\{#PayloadDirectory}\GrasshopperFilePreview.dll"
Type: files; Name: "{app}\versions\{#PayloadDirectory}\GrasshopperFilePreview.Setup.exe"
Type: files; Name: "{app}\versions\{#PayloadDirectory}\GrasshopperFilePreview.Setup.exe.config"
Type: files; Name: "{app}\versions\{#PayloadDirectory}\GrasshopperFilePreview.Diagnostics.exe"
Type: files; Name: "{app}\versions\{#PayloadDirectory}\GrasshopperFilePreview.Diagnostics.exe.config"
Type: files; Name: "{app}\versions\{#PayloadDirectory}\Grasshopper.ico"
Type: files; Name: "{app}\versions\{#PayloadDirectory}\GrasshopperXml.ico"
Type: files; Name: "{app}\versions\{#PayloadDirectory}\LICENSE"
Type: files; Name: "{app}\versions\{#PayloadDirectory}\THIRD_PARTY_NOTICES.md"
Type: dirifempty; Name: "{app}\versions\{#PayloadDirectory}"
Type: dirifempty; Name: "{app}\versions"

[Code]
var
  MaintenancePage: TInputOptionWizardPage;
  ExistingUninstaller, ExistingParameters: String;
  MaintenanceCompleted, Registering, Registered, InstallationComplete: Boolean;
  SetupExitCode: Integer;

function GetCustomSetupExitCode: Integer;
begin
  Result := SetupExitCode;
end;

function GetDefaultInstallDir(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{commonpf64}\GrasshopperFilePreview')
  else
    Result := ExpandConstant('{localappdata}\GrasshopperFilePreview');
end;

function ScopeLabel: String;
begin
  if IsAdminInstallMode then Result := 'All users'
  else Result := 'Only for me';
end;

function SelectedRoot: Integer;
begin
  if IsAdminInstallMode then Result := HKLM
  else Result := HKCU;
end;

function WorkerCommand(Command: String): String;
begin
  Result := Command;
  if IsAdminInstallMode then Result := Result + '-all-users';
end;

function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox('Grasshopper Thumbnails & Preview requires .NET Framework 4.8 or newer.',
      mbCriticalError, MB_OK, IDOK);
end;

procedure InitializeWizard;
var
  Folder: String;
begin
  if RegQueryStringValue(SelectedRoot, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\GrasshopperFilePreview.Bundle_is1',
    'UninstallString', ExistingUninstaller) then
    ExistingUninstaller := RemoveQuotes(ExistingUninstaller)
  else if not IsAdminInstallMode and RegQueryStringValue(HKCU, 'Software\GrasshopperBitmapPreview', 'InstallDirectory', Folder) then
  begin
    ExistingUninstaller := AddBackslash(Folder) + 'GrasshopperFilePreview.Setup.exe';
    ExistingParameters := '--uninstall';
  end;
  if FileExists(ExistingUninstaller) and not WizardSilent then
  begin
    MaintenancePage := CreateInputOptionPage(wpWelcome, 'Change installation',
      'Grasshopper Thumbnails & Preview is installed for this scope.', 'Installation scope: ' + ScopeLabel + '. Choose what you want to do:', True, False);
    MaintenancePage.Add('Install / update');
    MaintenancePage.Add('Uninstall');
    MaintenancePage.SelectedValueIndex := 0;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := 'Install thumbnails and previews for .gh and .ghx files.' + NewLine + NewLine +
    'Installation scope: ' + ScopeLabel + NewLine +
    'Location: ' + ExpandConstant('{app}') + NewLine + NewLine +
    'Setup refreshes Windows'' thumbnail cache for your account.' + NewLine +
    'Other thumbnails will rebuild as you browse.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode, ShowCommand: Integer;
begin
  Result := True;
  if MaintenancePage = nil then Exit;
  if (CurPageID <> MaintenancePage.ID) or (MaintenancePage.SelectedValueIndex <> 1) then Exit;
  Result := False;
  if (ExistingParameters <> '') and
    (MsgBox('Remove Grasshopper Thumbnails & Preview from this Windows account?', mbConfirmation, MB_YESNO) <> IDYES) then Exit;
  ShowCommand := SW_SHOWNORMAL;
  if ExistingParameters <> '' then ShowCommand := SW_HIDE;
  WizardForm.Hide;
  try
    if not Exec(ExistingUninstaller, ExistingParameters, '', ShowCommand, ewWaitUntilTerminated, ResultCode) then
      RaiseException('Could not start uninstall: ' + SysErrorMessage(ResultCode));
    if ResultCode <> 0 then Exit;
    MaintenanceCompleted := True;
    if ExistingParameters <> '' then
      MsgBox('Grasshopper Thumbnails & Preview has been uninstalled.', mbInformation, MB_OK);
  finally
    if MaintenanceCompleted then WizardForm.Close
    else WizardForm.Show;
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if Registering then Cancel := False;
  if MaintenanceCompleted then Confirm := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Output: TExecOutput;
begin
  Result := '';
  if Registered then Exit;
  // The worker uses a fixed location for each scope; reject a conflicting /DIR override.
  if CompareText(ExpandConstant('{app}'), GetDefaultInstallDir('')) <> 0 then
  begin
    Result := 'Grasshopper Thumbnails & Preview must use the default location for the selected scope. Run Setup without /DIR.';
    Exit;
  end;
  Log('Bundle extraction directory: ' + ExpandConstant('{tmp}'));
  ExtractTemporaryFile('GrasshopperFilePreview.dll');
  ExtractTemporaryFile('GrasshopperFilePreview.Setup.exe');
  ExtractTemporaryFile('GrasshopperFilePreview.Diagnostics.exe');
  ExtractTemporaryFile('GrasshopperFilePreview.Setup.exe.config');
  ExtractTemporaryFile('GrasshopperFilePreview.Diagnostics.exe.config');
  ExtractTemporaryFile('Grasshopper.ico');
  ExtractTemporaryFile('GrasshopperXml.ico');
  ExtractTemporaryFile('LICENSE');
  ExtractTemporaryFile('THIRD_PARTY_NOTICES.md');
  WizardForm.PreparingLabel.Caption := 'Enabling Explorer thumbnails and previews...';
  Registering := True;
  try
    if not ExecAndCaptureOutput(ExpandConstant('{tmp}\GrasshopperFilePreview.Setup.exe'),
      WorkerCommand('--install-managed') + ' "' + ExpandConstant('{tmp}\registration-rollback.xml') + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Output) then
      Result := 'Could not start installation: ' + SysErrorMessage(ResultCode)
    else if ResultCode <> 0 then
    begin
      Result := 'Could not enable Explorer thumbnails and previews.';
      if GetArrayLength(Output.StdErr) > 0 then
        Result := Result + #13#10 + Output.StdErr[0];
    end;
    Registered := Result = '';
  finally
    // Once registered, finish recording the installed files before allowing close.
    Registering := Registered;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  CacheRefreshed: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    InstallationComplete := True;
    Registering := False;
    if IsAdminInstallMode then
    begin
      // UAC may use a different administrator account. Refresh the original
      // user's cache through Inno's unelevated launcher, after files are installed.
      CacheRefreshed := False;
      ResultCode := -1;
      try
        CacheRefreshed := ExecAsOriginalUser(ExpandConstant('{app}\versions\{#PayloadDirectory}\GrasshopperFilePreview.Setup.exe'),
          '--refresh-cache', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      except
        Log('Could not launch thumbnail refresh: ' + GetExceptionMessage);
      end;
      if not CacheRefreshed or (ResultCode <> 0) then
      begin
        SetupExitCode := 1;
        Log('Installed for all users, but thumbnail cache refresh failed.');
        WizardForm.FinishedLabel.Caption := 'The app is installed for all users, but Windows could not refresh thumbnails for your account. Run Install / update again to retry.';
      end;
    end;
  end;
end;

procedure DeinitializeSetup;
var
  ResultCode: Integer;
  Output: TExecOutput;
begin
  if not Registered or InstallationComplete then Exit;
  Log('Rolling back handler registration after incomplete installation.');
  if ExecAndCaptureOutput(ExpandConstant('{tmp}\GrasshopperFilePreview.Setup.exe'),
    WorkerCommand('--rollback-managed') + ' "' + ExpandConstant('{tmp}\registration-rollback.xml') + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Output) then
    if ResultCode = 0 then
    begin
      Log('Previous handler registration restored.');
      Exit;
    end;
  Log('Could not restore the previous handler registration.');
  SuppressibleMsgBox('Setup could not restore the previous registration. Run the installer again to repair it.', mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Command: String;
begin
  if CurUninstallStep <> usUninstall then Exit;
  // A current-user uninstaller can itself be elevated. Use its recorded fixed
  // installation location, not the current token's privileges, to select the hive.
  Command := '--uninstall';
  if CompareText(ExpandConstant('{app}'), ExpandConstant('{commonpf64}\GrasshopperFilePreview')) = 0 then
    Command := '--uninstall-all-users';
  // After confirmation, before any installed files are deleted.
  if not Exec(ExpandConstant('{app}\versions\{#PayloadDirectory}\GrasshopperFilePreview.Setup.exe'),
    Command, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    SuppressibleMsgBox('Could not start removal. Run the installer to repair the installation.', mbError, MB_OK, IDOK);
    Abort;
  end;
  if ResultCode <> 0 then
  begin
    SuppressibleMsgBox('Could not remove the Explorer handlers. Close any other setup and try again.', mbError, MB_OK, IDOK);
    Abort;
  end;
end;
