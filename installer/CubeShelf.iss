; MyAppVersion / MyAppNumericVersion are fallbacks for compiling this script by hand.
; BUILD_INSTALLER.ps1 always passes /DMyAppVersion and /DMyAppNumericVersion from
; the VERSION file at the repository root, which is the single source of truth.
#ifndef MyAppVersion
  #define MyAppVersion "0.0-handbuilt"
#endif

#ifndef MyAppNumericVersion
  #define MyAppNumericVersion "0.0.0"
#endif

#ifndef PublishDirectory
  #define PublishDirectory "..\publish\CubeShelf"
#endif

#ifndef InstallerOutputDirectory
  #define InstallerOutputDirectory "..\dist"
#endif

[Setup]
AppId={{B1C7E383-AB82-44DC-B4B7-52753057CBA0}
AppName=CubeShelf
AppVersion={#MyAppVersion}
AppPublisher=zeranemesis
AppPublisherURL=https://github.com/zeranemesis/CubeShelf-Launcher
AppSupportURL=https://github.com/zeranemesis/CubeShelf-Launcher/issues
AppUpdatesURL=https://github.com/zeranemesis/CubeShelf-Launcher/releases
DefaultDirName={localappdata}\Programs\CubeShelf
DefaultGroupName=CubeShelf
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#InstallerOutputDirectory}
OutputBaseFilename=CubeShelf-Setup-x64
SetupIconFile=..\src\CubeShelf.Launcher\Assets\Brand\gamecube_logo.ico
UninstallDisplayIcon={app}\CubeShelf.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=CubeShelf.Launcher.SingleInstance.v1
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoCompany=CubeShelf
VersionInfoDescription=Installation de CubeShelf
VersionInfoProductName=CubeShelf

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le bureau"; GroupDescription: "Raccourcis :"; Flags: unchecked

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\CubeShelf"; Filename: "{app}\CubeShelf.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\CubeShelf"; Filename: "{app}\CubeShelf.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\CubeShelf.exe"; Description: "Lancer CubeShelf"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  // User data lives outside the application directory, under
  // LocalAppData\CubeShelf. Updates and uninstall preserve the game profile.
  Result := True;
end;
