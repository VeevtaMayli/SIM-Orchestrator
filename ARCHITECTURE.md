# SMS Gateway Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────┐
│                      SMS Gateway System                 │
└─────────────────────────────────────────────────────────┘

    SIM Card                                  Telegram
       │                                          ▲
       │ SMS                                      │
       ▼                                          │
┌──────────────┐     HTTPS POST            ┌──────────────┐
│  SIM-Relay   │─────────────────────────▶│ Orchestrator │
│   (ESP32)    │    X-API-Key + JSON       │  (ASP.NET)   │
│              │◀─────────────────────────│              │
│  LTE: SMS    │       200 OK              │  Database    │
│  WiFi: HTTP  │                           │  Telegram    │
└──────────────┘                           └──────────────┘
```

**Key point**: Device uses LTE modem for SMS only; HTTP goes via ESP32 WiFi.

## Data Flow

### 1. SMS Reception

```
Cellular Network → SIM Card → A7670 Modem → SmsManager
                                              │
                                              ▼
                                        PDU Parser (GSM7/UCS2 → UTF-8)
                                              │
                                              ▼
                                        SmsConcatenator (multi-part buffering)
```

### 2. Device → Server

```
SmsMessage → HttpSender → WiFiClientSecure → HTTPS POST
                                                │
                          ┌─────────────────────┘
                          ▼
                    ApiKeyMiddleware (validate X-API-Key)
                          │
                          ▼ 401 or continue
                    SmsController.ReceiveSms()
```

### 3. Server Processing

```
SmsController
     │
     ├──▶ SmsStorageService.SaveSmsAsync()
     │         └──▶ Database (SentToTelegram=false)
     │
     └──▶ TelegramService.SendAsync()
               │
               ├─ Success → MarkAsSent() → SentToTelegram=true
               └─ Failure → RetryService picks up later
```

### 4. Background Retry

```
RetryService (every 5 min)
     │
     └──▶ GetUnsentSms() → foreach → TelegramService → MarkAsSent()
```

## Error Handling

| Scenario | Device Behavior | Server Behavior |
|----------|-----------------|-----------------|
| Server unreachable | Keep SMS on SIM, retry in 10s | — |
| Server 500 | Keep SMS on SIM, resend | Resend is deduplicated by key, no second copy |
| Response lost after 200 | Resend | Server answers `duplicate` with the original id |
| Telegram down | — | SMS saved, dispatcher retries with backoff (1/5/15/60 min) |
| Telegram 429 | — | Retried after `retry_after`, batch paused |
| Telegram fails 20 times | — | Message `Failed`, `/health` degraded |
| Success | Delete SMS from SIM | Mark as sent |

## Security

| Layer | Mechanism |
|-------|-----------|
| Device → Server | HTTPS (TLS) + CA certificate validation |
| Authentication | X-API-Key header → ApiKeyMiddleware (SHA-256 + constant-time compare, several active keys for rotation) |
| Server → Telegram | HTTPS (TLS 1.2+) |
| Secrets | Environment variables / user secrets, validated at startup; bot token never logged |
| Abuse | 60 req/min per client IP (real IP taken from the reverse proxy), 64 KB request limit |

## Database Schema

```sql
SmsMessages
├── Id (PK)
├── Sender (varchar 50)
├── Text (text)
├── Timestamp (varchar 50)          -- as sent by the device
├── ReceivedAt (datetime, UTC)      -- indexed
├── DedupKey (varchar 80)           -- unique
├── Status (int)                    -- Pending/InProgress/Sent/Failed, indexed with NextAttemptAt
├── Attempts (int)
├── NextAttemptAt (datetime, nullable)
├── LeaseExpiresAt (datetime, nullable)
├── LastError (varchar 500, nullable)
├── SentToTelegram (bool)           -- legacy flag, equals Status = Sent
└── SentToTelegramAt (datetime, nullable)
```

## Technology Stack

| Component | SIM-Relay | SIM-Orchestrator |
|-----------|-----------|------------------|
| Platform | ESP32 | .NET 8.0 |
| HTTP | WiFiClientSecure | ASP.NET Core |
| SMS | TinyGSM + A7670 | — |
| Database | — | EF Core + SQLite/PostgreSQL |
| JSON | ArduinoJson | System.Text.Json |

## Configuration

### Device (SIM-Relay)

| Setting | File | Description |
|---------|------|-------------|
| WiFi credentials | secrets.h | SSID + password |
| Server URL | secrets.h | HOST + PORT |
| API Key | secrets.h | X-API-Key value |
| Check intervals | config.h | SMS/network timing |

### Server (SIM-Orchestrator)

| Setting | File | Description |
|---------|------|-------------|
| API Key(s) | `ApiKey` / `ApiKeys__N` env | Must match device, min 32 chars |
| Telegram | `Telegram__BotToken`, `Telegram__ChatId` env | Bot token + chat ID |
| Database | `ConnectionStrings__DefaultConnection` env | Connection string |
