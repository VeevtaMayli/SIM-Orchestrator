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
Cellular Network → SIM Card → A7670 Modem → SmsReader
                                              │
                                              ▼
                                        Parse UCS2→UTF-8
```

### 2. Device → Server

```
SmsMessage → HttpSender → WiFiClientSecure → HTTPS POST
                                                │
                          ┌─────────────────────┘
                          ▼
                    ApiKeyMiddleware (validate X-API-Key)
                          │
                          ▼ 401/403 or continue
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
| Server 500 | Keep SMS on SIM | SMS saved, RetryService retries Telegram |
| Telegram down | — | SMS saved, RetryService retries in 5 min |
| Success | Delete SMS from SIM | Mark as sent |

## Security

| Layer | Mechanism |
|-------|-----------|
| Device → Server | HTTPS (TLS) + CA certificate validation |
| Authentication | X-API-Key header → ApiKeyMiddleware |
| Server → Telegram | HTTPS (TLS 1.2+) |
| Secrets | Separate config files, git-ignored |

## Database Schema

```sql
SmsMessages
├── Id (PK)
├── Sender (varchar 50)
├── Text (text)
├── Timestamp (varchar 50)
├── ReceivedAt (datetime)
├── SentToTelegram (bool) -- indexed
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
| API Key | appsettings.json | Must match device |
| Telegram | appsettings.json | Bot token + chat ID |
| Database | appsettings.json | Connection string |
