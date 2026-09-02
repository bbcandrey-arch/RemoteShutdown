; Инсталлятор Remote Shutdown Agent (Inno Setup) — упаковывает то, что уже собрано в
; distrib/windows-agent/ (см. distrib/README.md — сборка через dotnet publish
; --self-contained --single-file). Сам этот .iss ничего не компилирует из исходников,
; только пакует готовые файлы, поэтому перед сборкой инсталлятора нужно сначала
; пересобрать distrib/windows-agent/ (см. windows-agent/installer/build.ps1).
;
; AppVersion ниже нужно вручную держать синхронной с windows-agent/Directory.Build.props
; (Version) — единого источника правды между .iss и .csproj в Inno Setup нет.
;
; v2: агент теперь ставится как Windows Service (RemoteShutdown.Agent.Service.exe) —
; поднимается при загрузке ОС от LocalSystem, ДО входа пользователя (см. AgentHost.cs,
; docs/roadmap.md, "Windows Service"). Отсюда PrivilegesRequired=admin (регистрация
; службы требует прав администратора) и установка в {commonappdata}, а не
; {localappdata} (SYSTEM не имеет доступа к профилю конкретного пользователя). Tray
; остаётся как раньше — автозагрузка через HKCU при входе, плюс мост к службе
; (SessionRelayClient) для команд, которым физически нужен рабочий стол.

#define MyAppName "Remote Shutdown Agent"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "vol.and"
#define MyAppExeName "RemoteShutdown.Agent.Tray.exe"
#define ServiceExeName "RemoteShutdown.Agent.Service.exe"
#define ServiceName "RemoteShutdownAgent"
#define SourceDir "..\..\distrib\windows-agent"

[Setup]
AppId={{B7E2B6C1-6F3A-4B8E-9C2D-9F1A7E5D3C40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; ProgramData — единственное место, доступное и Tray (обычный пользователь), и Service
; (LocalSystem) одинаково; agent.db уже жил тут и раньше (см. AgentDatabase.cs,
; CommonApplicationData), так что миграция данных при обновлении с v1 не нужна.
DefaultDirName={commonappdata}\RemoteShutdownAgent
DefaultGroupName={#MyAppName}
PrivilegesRequired=admin
; {sys} без этого указывает на SysWOW64 у 32-битного инсталлятора — оттуда, например,
; не находится ie4uinit.exe. Наши сборки только win-x64, так что 64-битный режим не
; ограничение, а просто корректность.
ArchitecturesInstallIn64BitMode=x64compatible
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
Name: "autostart"; Description: "Запускать при входе в Windows"; GroupDescription: "Автозагрузка:"; Flags: checkedonce

[Files]
; Все файлы из уже собранного distrib/windows-agent/ (Service.exe + Tray.exe, публикуются
; build.ps1), кроме .pdb (отладочные символы, в установке не нужны).
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
; включить). Служба поднимается сама при загрузке ОС независимо от этого — этот ключ
; только про иконку в трее конкретного пользователя.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "RemoteShutdownAgentTray"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart

[Run]
; Explorer кэширует иконку exe по пути к файлу и не всегда замечает, что exe был
; перезаписан новой версией — сбрасываем сами, тихо, без перезапуска explorer.exe.
Filename: "{sys}\ie4uinit.exe"; Parameters: "-ClearIconCache"; Flags: runhidden; StatusMsg: "Обновление кэша иконок Windows..."

; Брандмауэр НЕ трогаем здесь: порт настраиваемый (см. SettingsForm), а инсталлятор не
; знает, какой порт уже выбран на этой машине — заново создать правило с портом по
; умолчанию значило бы затереть уже верно настроенное правило под нестандартный порт.
; Кнопка "Добавить правило в брандмауэр" в настройках трея остаётся единственным
; источником истины для этого правила — она уже умеет читать реальный порт из БД.

; Регистрация службы — идемпотентно: сначала тихо снести то, что могло остаться от
; предыдущей установки (сама служба уже остановлена в InitializeSetup, до копирования
; файлов), потом создать заново с актуальным путём и запустить.
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; StatusMsg: "Регистрация службы агента..."
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""{app}\{#ServiceExeName}"" start= auto DisplayName= ""Remote Shutdown Agent"""; \
    Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Удалённое управление питанием ПК с телефона — docs/roadmap.md.""" ; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden; StatusMsg: "Запуск службы агента..."

Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Порядок важен: сначала остановить/удалить службу, потом убить трей — иначе трей может
; тут же попробовать переподключиться к только что убитой службе (не страшно, просто
; лишний шум в логе переподключения).
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "{sys}\cmd.exe"; Parameters: "/c taskkill /F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillTray"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Служба, если уже установлена предыдущей версией v2, держит Service.exe занятым —
  // без остановки шаг [Files] упадёт с "файл занят другим процессом". sc stop, если
  // службы ещё нет, просто вернёт ошибку — это не повод останавливать установку.
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500); // дать процессу службы реально завершиться после sc stop

  // На случай, если трей (или, для установок v1, старый Api.exe) уже запущен.
  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM RemoteShutdown.Agent.Tray.exe /IM RemoteShutdown.Agent.Api.exe /IM RemoteShutdown.Agent.Service.exe',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
