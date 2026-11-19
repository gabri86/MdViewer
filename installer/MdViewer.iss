#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{B6E0F4C2-5A3D-4E7B-9C1A-3D2F8A6E7B10}
AppName=MdViewer
AppVersion={#AppVersion}
AppPublisher=gabri86
DefaultDirName={autopf}\MdViewer
DefaultGroupName=MdViewer
UninstallDisplayIcon={app}\MdViewer.exe
SetupIconFile=..\Assets\app.ico
OutputDir=..\dist
OutputBaseFilename=MdViewer-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\MdViewer.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\MdViewer"; Filename: "{app}\MdViewer.exe"
Name: "{autodesktop}\MdViewer"; Filename: "{app}\MdViewer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MdViewer.exe"; Description: "{cm:LaunchProgram,MdViewer}"; Flags: nowait postinstall skipifsilent
