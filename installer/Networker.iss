#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif

[Setup]
AppId=Networker.Desktop
AppName=Networker
AppVersion={#AppVersion}
AppPublisher=NormalDudeBro
AppPublisherURL=https://github.com/NormalDudeBro/networker
AppSupportURL=https://github.com/NormalDudeBro/networker/issues
AppUpdatesURL=https://github.com/NormalDudeBro/networker/releases
DefaultDirName={localappdata}\Networker.Desktop
DefaultGroupName=Networker
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Networker-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Networker.exe
SetupLogging=yes
ChangesAssociations=no
AllowNoIcons=no
#ifdef SignToolName
SignTool={#SignToolName}
SignedUninstaller=yes
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\root\Networker.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\root\active-slot.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\app-a\*"; DestDir: "{app}\app-a"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Networker"; Filename: "{app}\Networker.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Networker"; Filename: "{app}\Networker.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Networker.exe"; Description: "Launch Networker"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\app-a"
Type: filesandordirs; Name: "{app}\app-b"
Type: files; Name: "{app}\active-slot.txt"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not IsAdminInstallMode then
    Log('Installing Networker per-user without elevation.');
end;
