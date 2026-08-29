# Архитектура и текущий статус

См. также: [protocol.md](protocol.md) (контракт запросов/ответов),
[security.md](security.md) (модель угроз, заглушка опасных действий),
[roadmap.md](roadmap.md) (план дальнейшей разработки).

## Компоненты

```
┌─────────────────────┐        HTTPS (самоподписанный сертификат,          ┌──────────────────────────┐
│   Mobile App         │        fingerprint pinning) + HMAC-подпись         │   Windows Agent            │
│   (Flutter, Android)  │◄───────────────────────────────────────────────►│   (.NET 9, Kestrel)         │
│                       │        WebSocket /events (метрики, push)          │                            │
└─────────────────────┘                                                    └──────────────────────────┘
```

Оба работают только в одной локальной сети. Облачного relay-сервера нет и не
планируется.

## Windows Agent (`windows-agent/`)

Решение `RemoteShutdown.Agent.sln`, `net9.0-windows`, 4 проекта:

- **RemoteShutdown.Agent.Core** — вся бизнес-логика без веб-слоя:
  - `Security/` — DPAPI-шифрование секретов, PBKDF2-хэш PIN, HMAC-подпись,
    кэш nonce (анти-replay), сервис pairing.
  - `Power/PowerActionsService.cs` — выполнение команд питания.
    **Содержит заглушку** `SimulateDangerousActions` — см.
    [security.md](security.md#заглушка-опасных-действий).
  - `Volume/VolumeControlService.cs` — громкость через `keybd_event`
    (VK_VOLUME_UP/DOWN/MUTE) — только +/-/mute, без точного уровня.
  - `Timers/` — модели, репозиторий SQLite, `TimerSchedulerService`
    (`System.Threading.Timer` в памяти на каждый ожидающий таймер + SQLite
    как источник истины, восстановление при старте).
  - `Metrics/MetricsCollector.cs`, `Network/NetworkInfoService.cs`.
  - `Storage/` — SQLite (`AgentDatabase`, `SettingsStore`); БД лежит в
    `%ProgramData%\RemoteShutdownAgent\agent.db`.
- **RemoteShutdown.Agent.Api** — HTTP/WS-слой:
  - `CertificateProvider.cs` — генерация и установка самоподписанного
    сертификата в `X509Store(CurrentUser\My)` (это обязательно для успешного
    TLS-хэндшейка через SChannel — см. security.md).
  - `Middleware/HmacAuthMiddleware.cs` — проверка подписи всех запросов
    кроме `/pair/*`.
  - `Endpoints/` — `PairEndpoints`, `CommandEndpoints`, `TimerEndpoints`,
    `StatusEndpoints`.
  - `Events/WebSocketEventHub.cs` + `MetricsBroadcastService.cs` — push
    события и периодическая рассылка метрик по WS `/events`.
  - `Program.cs` — DI, Kestrel HTTPS, middleware pipeline, восстановление
    таймеров при старте.
- **RemoteShutdown.Agent.Tray** (WinForms) — видимая часть агента для
  пользователя: иконка в трее, запускает `RemoteShutdown.Agent.Api.exe` как
  дочерний процесс, окно настроек (`SettingsForm`) с тремя вкладками:
  - Сопряжение — QR-код (`Core/Pairing/PairingQrService`), выбор сетевого
    интерфейса при нескольких IP, установка/просмотр PIN.
  - Устройства — список сопряжённых, отзыв доступа.
  - Общие — порт, тестовый режим (см. ниже), автозагрузка
    (`AutostartService`, реестр `HKCU\...\Run`).
  Запуск: сама иконка трея, либо `RemoteShutdown.Agent.Tray.exe --settings`
  чтобы сразу открыть окно.
- **RemoteShutdown.Agent.Service** — пока пустой плейсхолдер, зарезервирован
  под миграцию в Windows Service (см. roadmap, этап 5).
- **tests/RemoteShutdown.Agent.Core.Tests** — 19 юнит-тестов (HMAC, PIN,
  nonce-кэш, pairing, планировщик таймеров), все проходят.

Сейчас агент запускается вручную как консольное приложение
(`dotnet run --project src/RemoteShutdown.Agent.Api`), не как служба и без
автозапуска — трей-иконки и UI настроек ещё нет.

## Mobile App (`mobile-app/`)

Flutter, пакет `com.remoteshutdown.mobile_app`, Riverpod
(`flutter_riverpod`) для state management.

- `core/security/` — secure storage секрета (`flutter_secure_storage`),
  HMAC-подпись (Dart-зеркало серверной формулы), хранилище профиля
  устройства (`shared_preferences`).
- `core/network/` — `TlsPinningClient` (TOFU + строгий pinning через
  `badCertificateCallback`), `ApiClient` (`postUnsigned` для pairing,
  `getSigned`/`postSigned`/`patchSigned` для остального).
- `features/pairing/` — `PairingController` (sealed `PairingState`),
  `PairingScreen` — экран сопряжения (IP, порт, PIN), полностью на русском.
- `features/dashboard/` — `DashboardController`, `DashboardScreen` — статус
  ПК, команды питания с подтверждением для опасных, пресеты отложенного
  выключения (15/30/60 мин), громкость, список активных таймеров.
- `main.dart` — `_StartupGate` маршрутизирует на Dashboard или
  PairingScreen в зависимости от наличия сохранённого `DeviceProfile`.

## Что подтверждено живым тестированием

Полный сквозной сценарий пройден на этой машине (эмулятор Android + реальный
Windows Agent на хосте, `10.0.2.2:18789`): discovery вручную по IP → pairing
по PIN → HMAC-подписанная команда shutdown → сработала заглушка (calc.exe),
реального выключения не произошло.

Юнит-тесты: 19/19 на стороне .NET, 1/1 на стороне Flutter (виджет-тест
экрана сопряжения — полный `_StartupGate` в widget-тестах зависает из-за
отсутствия платформенного канала `shared_preferences`, поэтому тестируется
`PairingScreen` напрямую).

## Известные ограничения / технический долг этой машины

- Порт агента переопределён на `18789` вместо дефолтного `54321` — только
  в живой БД этой машины, потому что после включения Hyper-V/WHPX диапазон
  `54288-54387` оказался зарезервирован под динамический NAT
  (`netsh interface ipv4 show excludedportrange protocol=tcp`). Продуктовый
  дефолт в `docs/protocol.md` должен оставаться `54321`.
- Discovery (mDNS/UDP broadcast) ещё не реализован — только ручной ввод IP
  или QR-код (host+port), см. ниже.
- QR-код кодирует только host+port (не fingerprint/токен) — PIN всё ещё
  вводится отдельно, см. docs/security.md.
