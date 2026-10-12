; Inno Setup 6 安装包脚本 - Windows屏幕使用时间
; 编译：ISCC.exe installer\setup.iss → 产物 dist\Windows屏幕使用时间_Setup_<版本>.exe

#define MyAppName "Windows屏幕使用时间"
#define MyAppVersion "1.5.0"
#define MyAppPublisher "kbcrt"
#define MyAppExeName "Windows屏幕使用时间.exe"

[Setup]
AppId={{6A2E1D4C-9B3F-4E8A-8C7D-1F2B3A4C5D6E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Windows-ScreenTime_Setup_{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\ScreenTime.App\app.ico
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
; 用户数据（%LOCALAPPDATA%\ScreenTime）在卸载时保留，防止历史统计丢失

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl,ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"
Name: "autostart"; Description: "开机自动启动（登录后静默后台记录）"; GroupDescription: "附加任务:"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; 开机自启（HKCU，静默启动）
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ScreenTimeApp"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Tasks: autostart
; 卸载时清理自启
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "ScreenTimeApp"; Flags: uninsdeletevalue

[Run]
; 无 skipifsilent：静默自更新完成后也自动重启应用
Filename: "{app}\{#MyAppExeName}"; Description: "运行 {#MyAppName}"; Flags: nowait postinstall
