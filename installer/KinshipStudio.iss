; This identity and installation mode must stay unchanged across releases.
#ifndef AppVersion
  #define AppVersion "3.5.1"
#endif
#ifndef AppId
  #define AppId "KinshipStudio.GhaithSaadJawad"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef InstallerOutput
  #define InstallerOutput "..\artifacts"
#endif

[Setup]
AppId={#AppId}
AppName=Kinship Studio
AppVersion={#AppVersion}
AppPublisher=Ghaith Saad Jawad
AppPublisherURL=https://github.com/Karthus3560/monster-hunter-stories-2-trainer
AppSupportURL=https://github.com/Karthus3560/monster-hunter-stories-2-trainer/issues
AppUpdatesURL=https://github.com/Karthus3560/monster-hunter-stories-2-trainer/releases/latest
DefaultDirName={localappdata}\Programs\RidersToolkit
DefaultGroupName=Kinship Studio
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UsePreviousAppDir=yes
UninstallDisplayName=Kinship Studio
UninstallDisplayIcon={app}\RidersToolkit.exe
SetupMutex=KinshipStudio.Setup.{#AppId}
CloseApplications=yes
RestartApplications=no
SetupIconFile=..\navirou-pixel-v33.ico
WizardStyle=modern
Compression=lzma2/fast
SolidCompression=yes
OutputDir={#InstallerOutput}
OutputBaseFilename=KinshipStudio-v{#AppVersion}-Setup
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Kinship Studio
VersionInfoCompany=Ghaith Saad Jawad
LicenseFile=..\LICENSE
#ifdef KinshipSigning
SignTool=KinshipSign
SignedUninstaller=yes
#endif

[Files]
Source: "{#PublishDir}\RidersToolkit.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\navirou-pixel-v33.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
#ifndef InstallerTest
Name: "{userprograms}\Kinship Studio"; Filename: "{app}\RidersToolkit.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Kinship Studio (Stories 2)"; Filename: "{app}\RidersToolkit.exe"; WorkingDir: "{app}"; IconFilename: "{app}\navirou-pixel-v33.ico"
#endif

[Run]
Filename: "{app}\RidersToolkit.exe"; Description: "Open Kinship Studio"; Flags: nowait postinstall skipifsilent; Check: NormalLaunch
Filename: "{app}\RidersToolkit.exe"; Flags: nowait; Check: UpdateLaunch

[Code]
function HasSwitch(const Switch: String): Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Switch) = 0 then Result := True;
end;

function NormalLaunch: Boolean;
begin
  Result := not HasSwitch('/UPDATE') and not HasSwitch('/NOLAUNCH');
end;

function UpdateLaunch: Boolean;
begin
  Result := HasSwitch('/UPDATE') and not HasSwitch('/NOLAUNCH');
end;

function InitializeSetup: Boolean;
var Previous: String; OldVersion, NewVersion: Int64;
begin
  Result := True;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1', 'DisplayVersion', Previous) then
    if StrToVersion(Previous, OldVersion) and StrToVersion('{#AppVersion}', NewVersion) then
      if ComparePackedVersion(OldVersion, NewVersion) > 0 then begin
        SuppressibleMsgBox('A newer Kinship Studio version is already installed. Download the latest release instead.', mbError, MB_OK, IDOK);
        Result := False;
      end;
end;
