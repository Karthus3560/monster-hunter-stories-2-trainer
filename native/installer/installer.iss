#ifndef Payload
 #error Payload must point to the verified staging directory
#endif
#ifndef Output
 #define Output "output"
#endif
#ifdef TEST_BUILD
 #define AppGuid "{{55384410-AC5B-4CF8-AD14-D60E3FDFA1B6}"
 #define RegistryGuid "{55384410-AC5B-4CF8-AD14-D60E3FDFA1B6}"
 #define ProductName "Kinship Studio Launcher Fixture"
 #define SetupName "KinshipStudio-Launcher-Fixture-Setup"
#else
 #define AppGuid "{{E51718EA-3450-48D8-9C59-E51AE70E4D4B}"
 #define RegistryGuid "{E51718EA-3450-48D8-9C59-E51AE70E4D4B}"
 #define ProductName "Kinship Studio — Stories 2"
 #define SetupName "KinshipStudio-Launcher-1.1.0-Setup"
#endif
[Setup]
AppId={#AppGuid}
AppName={#ProductName}
AppVersion=1.1.0
AppPublisher=Ghaith Saad Jawad
DefaultDirName={userpf}\Kinship Studio Launcher
DefaultGroupName=Kinship Studio — Stories 2
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartApplications=no
AppMutex=Local\KinshipStudio.NativeLauncher.UI
UninstallDisplayName={#ProductName}
UninstallDisplayIcon={app}\KinshipStudioLauncher.exe
OutputDir={#Output}
OutputBaseFilename={#SetupName}
SetupIconFile=..\assets\studio.ico
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
InfoBeforeFile={#Payload}\Readme.txt

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#Payload}\KinshipStudioLauncher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\modules\KinshipNativeBattleV2.dll"; DestDir: "{app}\modules"; Flags: ignoreversion
Source: "{#Payload}\modules\KinshipCampIcon.dll"; DestDir: "{app}\modules"; Flags: ignoreversion
Source: "{#Payload}\modules\KinshipLoadBridge.dll"; DestDir: "{app}\modules"; Flags: ignoreversion
Source: "{#Payload}\modules\kinship-ui-atlas.png"; DestDir: "{app}\modules"; Flags: ignoreversion
Source: "{#Payload}\modules\kinship-emblem.png"; DestDir: "{app}\modules"; Flags: ignoreversion
Source: "{#Payload}\modules\manifest.json"; DestDir: "{app}\modules"; Flags: ignoreversion
Source: "{#Payload}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#Payload}\Readme.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\launcher-install.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\KinshipStudioLauncher.exe"; Flags: dontcopy
Source: "{#Payload}\launcher-install.json"; Flags: dontcopy

[Icons]
Name: "{group}\Play Stories 2 with Kinship Studio"; Filename: "{app}\KinshipStudioLauncher.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Kinship Studio - Play Stories 2"; Filename: "{app}\KinshipStudioLauncher.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\Uninstall Kinship Studio"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\KinshipStudioLauncher.exe"; Description: "Open Kinship Studio"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; MessageFile, OldFolder, OldRemoval: String; MessageText: AnsiString;
begin
 Result := '';
 ExtractTemporaryFile('KinshipStudioLauncher.exe'); ExtractTemporaryFile('launcher-install.json');
 MessageFile := ExpandConstant('{tmp}\kinship-launcher-install-check.txt');
 if not Exec(ExpandConstant('{tmp}\KinshipStudioLauncher.exe'), '--check-install "' + WizardDirValue + '" "' + MessageFile + '" "' + ExpandConstant('{tmp}\launcher-install.json') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then begin
  Result := 'Could not check the selected installation folder. Nothing was installed.'; exit;
 end;
 if Code <> 0 then begin
  if LoadStringFromFile(MessageFile, MessageText) then Result := String(MessageText) else Result := 'Installation checks failed.'; exit;
 end;
#ifndef TEST_BUILD
 if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7A745B07-5CB0-4C47-AF13-B8404EF11036}_is1', 'Inno Setup: App Path', OldFolder) then begin
  OldRemoval := OldFolder + '\KinshipStudio\uninstall\unins000.exe';
  if FileExists(OldRemoval) then begin
   if not Exec(OldRemoval, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code) then begin Result := 'Could not remove the older startup package. Your files were preserved.'; exit; end;
   if Code <> 0 then begin Result := 'The older startup package could not be removed. Close the game and try again.'; exit; end;
  end;
 end;
#endif
end;

function InitializeUninstall(): Boolean;
var Code: Integer; MessageFile: String; MessageText: AnsiString;
begin
 Result := False; MessageFile := ExpandConstant('{tmp}\kinship-launcher-remove-check.txt');
 if not Exec(ExpandConstant('{app}\KinshipStudioLauncher.exe'), '--check-remove "' + ExpandConstant('{app}') + '" "' + MessageFile + '" "-"', '', SW_HIDE, ewWaitUntilTerminated, Code) then begin MsgBox('Could not check the installation. Nothing was removed.', mbError, MB_OK); exit; end;
 if Code <> 0 then begin if LoadStringFromFile(MessageFile, MessageText) then MsgBox(String(MessageText), mbError, MB_OK) else MsgBox('Removal checks failed.', mbError, MB_OK); exit; end;
 Result := True;
end;

function InitializeSetup(): Boolean;
var Folder, Removal: String; Choice, Code: Integer;
begin
 Result := True; if WizardSilent then exit;
 if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#RegistryGuid}_is1', 'Inno Setup: App Path', Folder) then begin
  Removal := Folder + '\unins000.exe';
  if FileExists(Removal) then begin
   Choice := MsgBox('Kinship Studio is already installed.' + #13#10 + #13#10 + 'Yes: update or repair.' + #13#10 + 'No: uninstall, keeping settings and backups.' + #13#10 + 'Cancel: exit.', mbConfirmation, MB_YESNOCANCEL);
   if Choice = IDCANCEL then Result := False;
   if Choice = IDNO then begin Exec(Removal, '', '', SW_SHOW, ewWaitUntilTerminated, Code); Result := False; end;
  end;
 end;
end;
