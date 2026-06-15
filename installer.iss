#define MyAppName "DesktopPet"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "SixOfFive"
#define MyAppURL "https://github.com/SixOfFive/DesktopPet"
#define MyAppExeName "Neko.exe"

[Setup]
; A fixed AppId means future installers recognize an existing install and upgrade it cleanly.
AppId={{A8F3C2D4-9B1E-4F5A-BD6C-2E7F3A1D8C90}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; Allow per-user install by default; user can elevate to per-machine via the prompt.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=dist
OutputBaseFilename=DesktopPet-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

WizardStyle=modern
ShowLanguageDialog=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupshortcut"; Description: "Launch {#MyAppName} when Windows starts"; GroupDescription: "Startup:"

[Files]
Source: "bin\Release\net9.0-windows\win-x64\publish\Neko.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net9.0-windows\win-x64\publish\glfw3.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net9.0-windows\win-x64\publish\pets\*"; DestDir: "{app}\pets"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "kenney_cube-pets_1.0\License.txt"; DestDir: "{app}"; DestName: "Kenney-License.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupshortcut

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
