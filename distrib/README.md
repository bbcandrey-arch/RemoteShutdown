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
