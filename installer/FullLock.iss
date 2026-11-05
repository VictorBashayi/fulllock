; FullLock installer — built by installer/build.ps1 (iscc /DAppVersion=x.y.z FullLock.iss)
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{B992D2C4-8FBD-4E49-A771-F7A1A4823186}
AppName=FullLock
AppVersion={#AppVersion}
AppVerName=FullLock {#AppVersion}
AppPublisher=Victor Bashayi
AppPublisherURL=https://github.com/VictorBashayi/fulllock
AppSupportURL=https://github.com/VictorBashayi/fulllock/issues
DefaultDirName={autopf}\FullLock
DisableDirPage=yes
UsePreviousAppDir=no
DefaultGroupName=FullLock
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
OutputBaseFilename=FullLock-Setup-{#AppVersion}
UninstallDisplayIcon={app}\FullLock.exe
UninstallDisplayName=FullLock
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcuts"; Flags: unchecked
Name: "hidhide"; Description: "Install HidHide (recommended for DualShock 4 users — stops games seeing the pad twice)"; Flags: unchecked; Check: not HidHideInstalled

[Files]
Source: "out\publish\FullLock.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "deps\ViGEmBus_Setup.exe"; Flags: dontcopy
Source: "deps\HidHide_Setup.exe"; Flags: dontcopy

[Icons]
Name: "{group}\FullLock (Phone)"; Filename: "{app}\FullLock.exe"; WorkingDir: "{app}"
Name: "{group}\FullLock (DS4 pad)"; Filename: "{app}\FullLock.exe"; Parameters: "--ds4"; WorkingDir: "{app}"
Name: "{group}\Uninstall FullLock"; Filename: "{uninstallexe}"
Name: "{autodesktop}\FullLock (Phone)"; Filename: "{app}\FullLock.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{autodesktop}\FullLock (DS4 pad)"; Filename: "{app}\FullLock.exe"; Parameters: "--ds4"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Phone mode listens on UDP; allow it on Private networks only (never on public Wi-Fi)
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""FullLock"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""FullLock"" dir=in action=allow program=""{app}\FullLock.exe"" protocol=UDP localport=5555,20777 remoteip=localsubnet profile=private"; Flags: runhidden waituntilterminated; StatusMsg: "Adding firewall rule for phone mode..."

[UninstallRun]
Filename: "{app}\FullLock.exe"; Parameters: "--hidhide-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "HidHideCleanup"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""FullLock"""; Flags: runhidden waituntilterminated; RunOnceId: "FirewallRule"

[Code]
var
  RestartNeeded: Boolean;

function ServiceInstalled(const Name: String): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\' + Name);
end;

function ViGEmInstalled: Boolean;
begin
  Result := ServiceInstalled('ViGEmBus');
end;

function HidHideInstalled: Boolean;
begin
  Result := ServiceInstalled('HidHide');
end;

{ Runs a bundled driver installer silently. 3010 = success, reboot required. }
procedure InstallDriver(const FileName, DisplayName: String);
var
  ResultCode: Integer;
begin
  WizardForm.StatusLabel.Caption := 'Installing ' + DisplayName + ' driver...';
  ExtractTemporaryFile(FileName);
  if not Exec(ExpandConstant('{tmp}\') + FileName, '/exenoui /qn /norestart', '',
              SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    MsgBox('Could not start the ' + DisplayName + ' installer: ' + SysErrorMessage(ResultCode), mbError, MB_OK)
  else if ResultCode = 3010 then
    RestartNeeded := True
  else if ResultCode <> 0 then
    MsgBox(DisplayName + ' setup ended with code ' + IntToStr(ResultCode) + '.' + #13#10 +
           'FullLock may not work until ' + DisplayName + ' is installed. You can get it from' + #13#10 +
           'https://github.com/nefarius/' + DisplayName + '/releases/latest', mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not ViGEmInstalled then InstallDriver('ViGEmBus_Setup.exe', 'ViGEmBus');
    if WizardIsTaskSelected('hidhide') and not HidHideInstalled then InstallDriver('HidHide_Setup.exe', 'HidHide');
  end;
end;

function NeedRestart: Boolean;
begin
  Result := RestartNeeded;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox('Delete your FullLock settings and logs too?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\FullLock'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\FullLock'), True, True, True);
    end;
end;
