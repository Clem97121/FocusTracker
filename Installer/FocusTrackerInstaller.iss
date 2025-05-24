[Setup]
AppName=FocusTracker
AppVersion=1.0.0
DefaultDirName={pf}\FocusTracker
DefaultGroupName=FocusTracker
OutputBaseFilename=FocusTrackerInstaller
OutputDir=Output
Compression=lzma
SolidCompression=yes
SetupIconFile=C:\Projects\FocusTracker\FocusTracker.App\icon.ico
DisableProgramGroupPage=no

[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[Files]
; Копируем только publish-версию без мусора
Source: "C:\Projects\FocusTracker\FocusTracker.App\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\FocusTracker"; Filename: "{app}\FocusTracker.App.exe"
Name: "{group}\Uninstall FocusTracker"; Filename: "{uninstallexe}"
Name: "{commondesktop}\FocusTracker"; Filename: "{app}\FocusTracker.App.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Створити ярлик на робочому столі"; GroupDescription: "Додаткові параметри:"
