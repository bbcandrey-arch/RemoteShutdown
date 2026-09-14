# Протокол связи Mobile App ↔ Windows Agent

Версия: 0.1 (Этап 0 — контракт до реализации). Любое изменение здесь должно быть согласовано на обеих сторонах до правки кода.

## 1. Транспорт

- Windows Agent поднимает Kestrel HTTPS-сервер на LAN, порт по умолчанию **55765** (настраивается).
- Сертификат — самоподписанный, генерируется агентом при первом запуске (`%ProgramData%\RemoteShutdownAgent\cert.pfx`).
- REST — для запрос/ответ команд. WebSocket `wss://<host>:<port>/events` — для push-событий и live-метрик.
- Discovery UDP-порт (broadcast fallback): **54322**.

## 2. Discovery

### mDNS
Сервис публикуется как `_pcshutdown._tcp.local`, TXT-записи: `id=<deviceId>`, `port=55765`, `fp=<sha256 сертификата>`.

### UDP broadcast fallback
Запрос на broadcast-адрес порта 54322:
```json
{ "type": "DISCOVER_REQUEST" }
```
Ответ агента (unicast обратно отправителю):
```json
{
  "type": "DISCOVER_RESPONSE",
  "deviceId": "guid",
  "hostname": "DESKTOP-ABC123",
  "port": 55765,
  "fingerprint": "sha256:...",
  "version": "0.1.0"
}
```

## 3. Формат сообщений

### REST — тело запроса
Тело запроса — это просто payload эндпоинта (см. таблицы в §6/§7), без обёртки. `requestId` генерируется клиентом и передаётся отдельным заголовком `X-Request-Id` (не частью подписываемых данных — нужен только для трассировки в ответе/логах).

Аутентификационные метаданные (`timestamp`, `nonce`, `signature`) идут заголовками, а не полями тела — иначе подпись пришлось бы вычислять над телом, уже содержащим саму подпись, что циклично. Заголовки и точная формула подписи — в §5.

`pair/init` и `pair/confirm` — единственные эндпоинты без заголовков аутентификации (на этапе pairing `sharedSecret` ещё не существует); их защищает только TLS-канал и сам PIN.

### REST — конверт ответа
```json
{
  "requestId": "guid",
  "status": "ok",
  "data": { }
}
```
Ошибка:
```json
{
  "requestId": "guid",
  "status": "error",
  "error": { "code": "INVALID_SIGNATURE", "message": "..." }
}
```

### WebSocket — события
```json
{ "type": "event", "event": "metricsUpdate", "data": { } }
```

## 4. Pairing

1. `POST /pair/init` — payload: `{ "deviceName": "Pixel 8" }` → ответ: `{ "pairingSessionId": "guid" }` (без подписи — сессии pairing ещё нет секрета).
2. Пользователь получает PIN (либо сканирует QR с host+port+fingerprint+pairingSessionId).
3. `POST /pair/confirm` — payload: `{ "pairingSessionId": "guid", "pin": "123456" }` → при успехе:
```json
{
  "status": "ok",
  "data": {
    "clientId": "guid",
    "sharedSecret": "base64, 256-bit",
    "deviceMac": "AA:BB:CC:DD:EE:FF",
    "broadcastHint": "192.168.1.255"
  }
}
```
4. Неверный PIN: `error.code = "INVALID_PIN"`. После 5 неудачных попыток подряд — `error.code = "PIN_LOCKED"` на 60 секунд (rate limiting).

## 5. Аутентификация обычных запросов

Заголовки:
- `X-Client-Id: <clientId>`
- `X-Timestamp: <unix ms>`
- `X-Nonce: <guid>`
- `X-Signature: <base64 HMAC-SHA256>`

```
signature = HMAC_SHA256(sharedSecret, method + "\n" + path + "\n" + timestamp + "\n" + nonce + "\n" + body)
```

Правила проверки на агенте:
- `|now - timestamp| > 180000ms` (3 минуты — см. docs/security.md, почему не 30с) → `error.code = "STALE_REQUEST"`.
- Повтор `nonce` для данного `clientId` в пределах окна валидности → `error.code = "REPLAY_DETECTED"`.
- Неизвестный/отозванный `clientId` → HTTP 401, `error.code = "UNKNOWN_CLIENT"`.

## 6. Команды

| Endpoint | Payload | Ответ data |
|---|---|---|
| `POST /commands/shutdown` | `{ "delaySeconds": 0 }` | `{ "scheduledAtUtc" }` |
| `POST /commands/restart` | `{ "delaySeconds": 0 }` | `{ "scheduledAtUtc" }` |
| `POST /commands/sleep` | `{}` | `{}` |
| `POST /commands/hibernate` | `{}` | `{}` |
| `POST /commands/lock` | `{}` | `{}` |
| `POST /commands/volume` | `{ "action": "up"\|"down"\|"mute"\|"unmute" }` | `{ "level": 42\|null, "muted": true\|false\|null }` |

`level`/`muted` are best-effort: the MVP implementation simulates the hardware volume keys rather than reading the audio endpoint precisely, so `level` may come back `null` (see `VolumeControlService` remarks in windows-agent). Clients must not rely on `level` being present.

## 7. Таймеры

| Endpoint | Payload | Ответ |
|---|---|---|
| `POST /timers` | `{ "action": "shutdown", "delaySeconds": 900 }` или `{ "action": "shutdown", "scheduledAtUtc": "..." }` | `{ "timerId", "scheduledAtUtc" }` |
| `GET /timers` | — | `{ "timers": [ { "timerId", "action", "scheduledAtUtc", "status" } ] }` |
| `PATCH /timers/{id}` | `{ "action": "cancel" }` \| `{ "action": "reschedule", "scheduledAtUtc": "..." }` \| `{ "action": "snooze", "minutes": 10 }` | `{ "timerId", "scheduledAtUtc", "status" }` |

Статусы таймера: `pending`, `fired`, `cancelled`, `missed`.

## 8. Статус и метрики

`GET /status`:
```json
{
  "online": true,
  "hostname": "DESKTOP-ABC123",
  "deviceMac": "AA:BB:CC:DD:EE:FF",
  "agentVersion": "0.1.0",
  "uptimeSeconds": 12345,
  "volume": { "level": 42, "muted": false },  // level may be null, see §6 note
  "pairedDevicesCount": 2
}
```

`GET /metrics`:
```json
{
  "cpuPercent": 12.3,
  "ramUsedBytes": 8500000000,
  "ramTotalBytes": 17000000000,
  "disks": [ { "drive": "C:", "usedBytes": 1, "totalBytes": 2 } ]
}
```

WS `metricsUpdate` — тот же payload, event-обёрнутый, интервал по умолчанию 5 сек.

## 9. Коды ошибок (сводно)

`INVALID_PIN`, `PIN_LOCKED`, `INVALID_SIGNATURE`, `STALE_REQUEST`, `REPLAY_DETECTED`, `UNKNOWN_CLIENT`, `TIMER_NOT_FOUND`, `HIBERNATE_NOT_SUPPORTED`, `INTERNAL_ERROR`.

## 10. Wake-on-LAN (вне протокола агента)

Magic packet отправляется телефоном напрямую по UDP (порт 9) на `broadcastHint`, содержимое: 6×`0xFF` + 16×MAC (полученный при pairing). Агент в этом обмене не участвует (недоступен, пока ПК выключен).
