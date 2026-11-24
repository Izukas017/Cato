; Instalador de Cato (Inno Setup 6).
; Antes: dotnet publish -c Release -r win-x64 --self-contained true -o artifacts\publish
; Luego: ISCC.exe installer\Cato.iss   ->  artifacts\CatoSetup-<versión>.exe

#define AppName "Cato"
#define AppVersion "0.2.0"
#define AppExe "Cato.exe"

[Setup]
AppId={{6B0F3C1E-8A2D-4C55-9E71-3D2A7F0C4B19}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Izukas017
AppPublisherURL=https://github.com/Izukas017/Cato
AppSupportURL=https://github.com/Izukas017/Cato/issues
; per-user install: no admin rights, goes to %LOCALAPPDATA%\Programs\Cato
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=CatoSetup-{#AppVersion}
SetupIconFile=..\Assets\cato.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; close a running Cato so its files can be replaced
CloseApplications=force
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Iniciar Cato con Windows"; GroupDescription: "Opciones:"; Flags: unchecked

[InstallDelete]
; clear any previous copy in the install folder (e.g. a manual or framework-dependent build)
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Cato"; \
    ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopCato"
; Cato's own "Iniciar con Windows" setting writes the same Run value; remove it even if the installer didn't create it
Filename: "{sys}\reg.exe"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Cato /f"; Flags: runhidden; RunOnceId: "RemoveRunKey"

[UninstallDelete]
; anything left in the install folder (user settings live in %APPDATA%\Cato and are kept)
Type: files; Name: "{app}\*"
