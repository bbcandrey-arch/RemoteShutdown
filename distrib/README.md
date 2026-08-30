# Дистрибутив

Готовые к запуску сборки — не требуют установленного .NET SDK или Flutter,
можно просто скопировать и запустить.

**Файлы сюда не коммитятся в git** (см. `.gitignore` в корне репозитория) —
слишком тяжёлые для истории. Пересобираются командами ниже по мере
необходимости; актуальность конкретных файлов на диске — на момент их
последней сборки, смотрите дату изменения файла.

## windows-agent/

Самодостаточные (`--self-contained`, `PublishSingleFile`) exe под win-x64,
не требуют установленного .NET на целевой машине:

- `RemoteShutdown.Agent.Tray.exe` — запускать этот. Поднимает иконку в
  трее и сам находит с запускает `RemoteShutdown.Agent.Api.exe` из той же
  папки как дочерний процесс.
- `RemoteShutdown.Agent.Api.exe` — можно тоже запустить напрямую (без
  трея), но тогда PIN/порт/тестовый режим можно менять только прямой
  правкой SQLite (`%ProgramData%\RemoteShutdownAgent\agent.db`).

Пересобрать:
```bash
cd windows-agent
dotnet publish src/RemoteShutdown.Agent.Api/RemoteShutdown.Agent.Api.csproj  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ../distrib/windows-agent
dotnet publish src/RemoteShutdown.Agent.Tray/RemoteShutdown.Agent.Tray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ../distrib/windows-agent
```

**Важно**: если publish делался в тот же `-o` без чистки — иногда (несколько
раз ловили на практике) он инкрементально пропускает нативные зависимости
(`e_sqlite3.dll`, `aspnetcorev2_inprocess.dll`), и агент падает при первом
обращении к SQLite. Если сомневаетесь — удалите `bin/`/`obj/` у
`RemoteShutdown.Agent.Api`/`.Core`/`.Tray` и `distrib/windows-agent/` перед
publish, либо просто используйте `installer/build.ps1` ниже — он публикует
с нуля каждый раз.

### installer/ — инсталлятор (Inno Setup)

`distrib/installer/RemoteShutdownAgent-Setup-<версия>.exe` — мастер
установки для тех, кому не нужен ручной xcopy: ставит в
`%LocalAppData%\RemoteShutdownAgent` (без прав администратора), создаёт
ярлыки в Пуск и (по желанию) на рабочем столе, умеет добавить автозагрузку
при установке, есть деинсталлятор. Данные агента (`%ProgramData%\...\agent.db`)
при удалении **не трогает** — сопряжения и таймеры переживают
переустановку/обновление.

Пересобрать (публикует agent заново и сразу компилирует инсталлятор):
```powershell
# Требует Inno Setup (winget install JRSoftware.InnoSetup), если ISCC.exe
# не в стандартном месте — windows-agent/installer/build.ps1 -IsccPath "..."
powershell -ExecutionPolicy Bypass -File windows-agent/installer/build.ps1
```
Версию в `windows-agent/installer/setup.iss` (`MyAppVersion`) нужно вручную
держать синхронной с `windows-agent/Directory.Build.props` (`Version`) —
общего источника версии между .iss и .csproj в Inno Setup нет.

## mobile-app/

Release APK, по одному на архитектуру процессора (меньше по размеру, чем
универсальный):

- `remote-shutdown-mobile-arm64-v8a-release.apk` — почти все современные
  Android-телефоны (последние ~7 лет).
- `remote-shutdown-mobile-armeabi-v7a-release.apk` — старые/бюджетные
  устройства.
- `remote-shutdown-mobile-x86_64-release.apk` — Android-эмуляторы,
  некоторые x86-планшеты.

Установка: перенести APK на телефон, разрешить "установку из неизвестных
источников", открыть файл.

Пересобрать:
```bash
cd mobile-app
flutter build apk --release --split-per-abi
cp build/app/outputs/flutter-apk/app-arm64-v8a-release.apk   ../distrib/mobile-app/remote-shutdown-mobile-arm64-v8a-release.apk
cp build/app/outputs/flutter-apk/app-armeabi-v7a-release.apk ../distrib/mobile-app/remote-shutdown-mobile-armeabi-v7a-release.apk
cp build/app/outputs/flutter-apk/app-x86_64-release.apk      ../distrib/mobile-app/remote-shutdown-mobile-x86_64-release.apk
```

## ⚠️ Напоминание

Тестовый режим (`SimulateDangerousActions`/`test_mode`) включён по
умолчанию на новой БД — команды выключения/перезагрузки/сна открывают
калькулятор вместо реального действия. См.
[../docs/security.md](../docs/security.md).
