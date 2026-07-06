#define DefaultAppVersion "0.1.1"
#define DefaultSourceDir "..\artifacts\package\publish"
#define DefaultOutputDir "..\artifacts\package\installer"
#define DefaultSetupIconFile "..\src\GenshinChatTranslator.App\Assets\logo.ico"

#ifndef AppVersion
#define AppVersion DefaultAppVersion
#endif

#ifndef SourceDir
#define SourceDir DefaultSourceDir
#endif

#ifndef OutputDir
#define OutputDir DefaultOutputDir
#endif

#ifndef SetupIconFile
#define SetupIconFile DefaultSetupIconFile
#endif

[Setup]
AppId={{7F7D0F8C-AE9C-4B35-BE8F-4217B545D61E}
AppName=Genshin Chat Translator
AppVersion={#AppVersion}
AppPublisher=shadyleaf
DefaultDirName={autopf}\Genshin Chat Translator
DefaultGroupName=Genshin Chat Translator
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=GenshinChatTranslator-Setup-{#AppVersion}
SetupIconFile={#SetupIconFile}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\GenshinChatTranslator.App.exe

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Genshin Chat Translator"; Filename: "{app}\GenshinChatTranslator.App.exe"
Name: "{autodesktop}\Genshin Chat Translator"; Filename: "{app}\GenshinChatTranslator.App.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{app}\GenshinChatTranslator.App.exe"; Description: "Launch Genshin Chat Translator"; Flags: nowait postinstall skipifsilent
