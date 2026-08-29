# Remote Shutdown — удалённое управление питанием ПК с телефона

Два приложения:

- **windows-agent** (.NET 9) — фоновый сервер на управляемом ПК: принимает
  команды, хранит таймеры, отдаёт метрики.
- **mobile-app** (Flutter) — Android-клиент: пейринг, дашборд, таймеры,
  громкость.

Связь — только по локальной Wi-Fi сети, без облачного релея. Подробности
протокола — [docs/protocol.md](docs/protocol.md), архитектура и статус —
[docs/architecture.md](docs/architecture.md), план на будущее —
[docs/roadmap.md](docs/roadmap.md), модель угроз — [docs/security.md](docs/security.md).

## ⚠️ Важно: заглушка опасных действий

Сейчас `PowerActionsService.SimulateDangerousActions = true`
([windows-agent/src/RemoteShutdown.Agent.Core/Power/PowerActionsService.cs](windows-agent/src/RemoteShutdown.Agent.Core/Power/PowerActionsService.cs)).
Это значит: команды shutdown/restart/sleep/hibernate **не выполняются по-настоящему** —
вместо них на ПК открывается калькулятор (`calc.exe`). Это сделано намеренно
после случая, когда тестовый таймер реально выключил рабочий компьютер.

**Не переключайте флаг в `false` на рабочей машине.** Переключать только на
одноразовой тестовой ВМ, когда нужно проверить реальное выполнение команд.
См. подробности в [docs/security.md](docs/security.md#заглушка-опасных-действий).

## Быстрый старт (разработка)

### Windows Agent
```bash
cd windows-agent
dotnet build RemoteShutdown.Agent.sln
dotnet test                                   # 19 юнит-тестов
dotnet run --project src/RemoteShutdown.Agent.Api
```
PIN и порт сейчас настраиваются через прямую правку `Settings` в
`%ProgramData%\RemoteShutdownAgent\agent.db` (SQLite) — трей-UI для этого
ещё не реализован, см. roadmap.

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
/docs               протокол, архитектура, roadmap, security
/windows-agent       .NET решение (Core / Api / Service / Tests)
/mobile-app          Flutter-приложение
```
