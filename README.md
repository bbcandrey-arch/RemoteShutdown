# Выключатель ПК — удалённое управление питанием ПК с телефона

Управляйте питанием домашнего/рабочего ПК с Android-телефона: выключение,
перезагрузка, сон, гибернация, блокировка экрана, таймеры отложенного
выключения, громкость, медиаклавиши и тачпад (мышь + клавиатура). Только по
локальной Wi-Fi сети — без облака, без регистрации, без интернета.

**→ [Инструкция для пользователя](docs/user-guide.md)** — установка, первое
подключение, решение проблем.

**→ [Releases](https://github.com/bbcandrey-arch/RemoteShutdown/releases)** —
готовые сборки: инсталлятор Windows-агента и APK для Android.

## Из чего состоит

- **windows-agent** (.NET 9) — работает как служба Windows (`RemoteShutdownAgent`,
  постоянно в фоне, поднимается вместе с ПК ещё до входа пользователя) плюс
  трей-компаньон (`RemoteShutdown.Agent.Tray`) для UI-настроек, QR-пейринга и
  команд, которым физически нужен открытый рабочий стол (громкость, медиа,
  блокировка, тачпад) — они переходят в трей через именованный канал
  (`SessionRelayServer`/`SessionRelayClient`).
- **mobile-app** (Flutter) — Android-клиент: пейринг по QR/PIN, дашборд,
  таймеры, громкость/медиа, тачпад, поддержка нескольких сопряжённых ПК.

Протокол — [docs/protocol.md](docs/protocol.md), архитектура —
[docs/architecture.md](docs/architecture.md), план развития —
[docs/roadmap.md](docs/roadmap.md), модель угроз —
[docs/security.md](docs/security.md).

## ⚠️ Тестовый режим (заглушка опасных действий)

`PowerActionsService.SimulateDangerousActions` — настройка (не константа в
коде), переключается в трее (Настройки → Общие → «Тестовый режим»). Пока
включён — shutdown/restart/sleep/hibernate не выполняются по-настоящему,
вместо них на ПК открывается калькулятор. **По умолчанию на новой/незнакомой
БД он выключен** (см. `AgentDefaults.cs`) — это боевой инструмент, а не
песочница по умолчанию; включайте его сами, когда нужно проверить поведение
не рискуя реальным выключением рабочей машины. Подробности —
[docs/security.md](docs/security.md#заглушка-опасных-действий).

## Быстрый старт (разработка)

### Windows Agent
```bash
cd windows-agent
dotnet build RemoteShutdown.Agent.sln
dotnet test                                        # юнит-тесты Core
dotnet run --project src/RemoteShutdown.Agent.Api   # интерактивный запуск для разработки
```
`RemoteShutdown.Agent.Api` — dev-удобство (`dotnet run`, без установки
службы); в собранном дистрибутиве роль постоянно запущенного хоста берёт на
себя `RemoteShutdown.Agent.Service` (см. `windows-agent/installer/build.ps1`
для сборки инсталлятора). Оба используют один и тот же `AgentHost.CreateApp`.

При первом запуске порт/PIN/имя ПК генерируются автоматически
(`AgentDefaults.cs`) — сопряжение с телефона работает сразу, без ручной
настройки БД.

### Mobile App
```bash
cd mobile-app
flutter pub get
flutter test
flutter run            # реальное устройство/эмулятор в той же Wi-Fi сети
```
На эмуляторе Android хост-машина доступна по адресу `10.0.2.2`.

## Структура репозитория

```
/docs               user-guide, протокол, архитектура, roadmap, security
/windows-agent       .NET решение (Core / Api / Service / Tray / Tests)
/mobile-app          Flutter-приложение
```
