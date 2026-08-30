; Инсталлятор Remote Shutdown Agent (Inno Setup) — упаковывает то, что уже собрано в
; distrib/windows-agent/ (см. distrib/README.md — сборка через dotnet publish
; --self-contained --single-file). Сам этот .iss ничего не компилирует из исходников,
; только пакует готовые файлы, поэтому перед сборкой инсталлятора нужно сначала
; пересобрать distrib/windows-agent/ (см. windows-agent/installer/build.ps1).
;
; AppVersion ниже нужно вручную держать синхронной с windows-agent/Directory.Build.props
; (Version) — единого источника правды между .iss и .csproj в Inno Setup нет.

#define MyAppName "Remote Shutdown Agent"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "vol.and"
#define MyAppExeName "RemoteShutdown.Agent.Tray.exe"
#define SourceDir "..\..\distrib\windows-agent"

[Setup]
AppId={{B7E2B6C1-6F3A-4B8E-9C2D-9F1A7E5D3C40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Ставим в LocalAppData пользователя — не требует прав администратора ни на установку,
; ни на обычную работу агента (админ нужен разово только для кнопки "Добавить правило
; в брандмауэр" в самом приложении, через отдельный UAC-запрос).
DefaultDirName={localappdata}\RemoteShutdownAgent
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
OutputDir=..\..\distrib\installer
OutputBaseFilename=RemoteShutdownAgent-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительные значки:"
Name: "autostart"; Description: "Запускать при входе в Windows"; GroupDescription: "Автозагрузка:"

[Files]
; Все файлы из уже собранного distrib/windows-agent/, кроме .pdb (отладочные символы,
; в установке не нужны) — см. windows-agent/installer/build.ps1, который гоняет
; dotnet publish перед вызовом ISCC.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Настройки {#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--settings"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Тот же ключ и то же имя значения, что использует AutostartService.cs в самом
; приложении (HKCU\...\Run, "RemoteShutdownAgentTray") — если пользователь позже
; переключит автозагрузку в настройках трея, это никак не конфликтует с тем, что
; поставил инсталлятор здесь (одна и та же запись реестра, просто два места её
; включить).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "RemoteShutdownAgentTray"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Останавливаем оба процесса перед удалением файлов — иначе занятые exe/dll не удалятся.
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM RemoteShutdown.Agent.Tray.exe /IM RemoteShutdown.Agent.Api.exe"; \
    Flags: runhidden; RunOnceId: "KillAgentProcesses"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // На случай, если агент уже запущен из предыдущей установки/распакованной
  // xcopy-версии — иначе Files-шаг упадёт с "файл занят другим процессом".
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM RemoteShutdown.Agent.Tray.exe /IM RemoteShutdown.Agent.Api.exe',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
