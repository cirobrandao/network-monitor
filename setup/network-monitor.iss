; Instalador por usuario (sem administrador).
; Compilado por scripts\pack-installer.ps1

#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif

#ifndef DistDir
  #define DistDir "..\dist"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "Network Monitor"
#define AppPublisher "Ciro Brandao"
#define AppURL "https://cirobrandao.com.br/"
#define AppExe "NetworkMonitor.exe"


[Setup]
AppId={{B8E4C21A-7F53-4A19-9D2E-81C0B4E17A02}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL=https://github.com/cirobrandao/network-monitor
AppUpdatesURL=https://github.com/cirobrandao/network-monitor/releases
DefaultDirName={localappdata}\NetworkMonitor
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\NetworkMonitor\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=NetworkMonitor-Setup-{#AppVersion}
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
UsedUserAreasWarning=no
AppMutex=Local\NetworkMonitor.SingleInstance

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; GroupDescription: "Atalhos:"; Flags: checkedonce
Name: "startup"; Description: "Iniciar com o Windows"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "{#DistDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "screenshot-*.png,*.pdb"

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Monitor de internet para Windows"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Monitor de internet para Windows"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NetworkMonitor"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o Network Monitor agora"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/IM NetworkMonitor.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
