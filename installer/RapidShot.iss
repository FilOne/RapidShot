#ifndef AppVersion
  #define AppVersion "1.25.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\RapidShot"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist\Installer"
#endif

[Setup]
AppId={{3BEF2EBC-4C44-4CFB-9E5D-0E33A9FA118E}
AppName=RapidShot
AppVersion={#AppVersion}
AppPublisher=WhiteHill
AppPublisherURL=https://gitlab.com/whitehillteam/rapidshot
AppSupportURL=https://gitlab.com/whitehillteam/rapidshot/-/issues
DefaultDirName={localappdata}\Programs\RapidShot
DefaultGroupName=RapidShot
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir={#OutputDir}
OutputBaseFilename=RapidShot-Setup-{#AppVersion}-x64
SetupIconFile=assets\RapidShot.ico
UninstallDisplayIcon={app}\RapidShot.exe
UninstallDisplayName=RapidShot
Compression=lzma2/normal
SolidCompression=yes
AppMutex=Local\RapidShot.Desktop.v1
CloseApplications=no
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoCompany=WhiteHill
VersionInfoDescription=RapidShot Setup

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\RapidShot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\ocr\*"; DestDir: "{app}\ocr"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\RapidShot\RapidShot"; Filename: "{app}\RapidShot.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\RapidShot"; Filename: "{app}\RapidShot.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\RapidShot.exe"; Description: "{cm:LaunchProgram,RapidShot}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    DeleteFile(ExpandConstant('{app}\ocr\tesseract55.dll'));
    DeleteFile(ExpandConstant('{app}\ocr\leptonica-1.85.0.dll'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usPostUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RapidShot', StartupCommand) then
      if CompareText(StartupCommand, '"' + ExpandConstant('{app}\RapidShot.exe') + '" --autostart') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RapidShot');
end;
