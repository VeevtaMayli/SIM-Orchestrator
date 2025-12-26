# SIM Gateway System Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    SMS Gateway Ecosystem                         │
└─────────────────────────────────────────────────────────────────┘

┌──────────────┐      HTTP POST         ┌─────────────────────┐
│  SIM-Relay   │─────────────────────────▶│  SIM-Orchestrator  │
│  (ESP32)     │   JSON: {sender,        │  (C# ASP.NET)      │
│              │          text,          │                     │
│  - A7670X    │          timestamp}     │  - Receives SMS     │
│  - TinyGSM   │                         │  - Stores in DB     │
│  - HTTP      │◀────────────────────────│  - Sends to TG      │
│              │      200 OK / 500       │  - Retry logic      │
└──────┬───────┘                         └──────────┬──────────┘
       │                                            │
       │ AT Commands                                │ HTTPS
       │ SMS Read/Delete                            │
       │                                            │
       ▼                                            ▼
┌──────────────┐                         ┌─────────────────────┐
│  SIM Card    │                         │  Telegram Bot API   │
│              │                         │                     │
│  - Receives  │                         │  - sendMessage      │
│    SMS       │                         │  - Chat/Channel     │
│  - GPRS/LTE  │                         │                     │
│    Data      │                         │                     │
└──────────────┘                         └─────────────────────┘
```

## Data Flow

### 1. SMS Arrival → Device

```
Cellular Network
      │
      │ SMS arrives on SIM
      ▼
┌─────────────────────────┐
│  SIM Card Storage       │
│  (30-50 SMS capacity)   │
└─────────────────────────┘
      │
      │ AT+CMGL="REC UNREAD" (every 10s)
      ▼
┌─────────────────────────┐
│  SmsReader              │
│  - Detects new SMS      │
│  - Reads via AT+CMGR    │
│  - Decodes UCS2→UTF-8   │
└─────────────────────────┘
```

### 2. Device → Server

```
┌─────────────────────────┐
│  HttpSender             │
│  - Creates JSON         │
│  - POST /api/sms        │
│  - Waits for 200 OK     │
└─────────────────────────┘
      │
      │ HTTP POST
      │ Content-Type: application/json
      │ {
      │   "sender": "+79991234567",
      │   "text": "Текст сообщения",
      │   "timestamp": "2025-12-26 14:30:15"
      │ }
      ▼
┌─────────────────────────┐
│  SmsController.cs       │
│  POST /api/sms          │
│  - Validates request    │
│  - Calls services       │
│  - Returns 200/500      │
└─────────────────────────┘
```

### 3. Server Processing

```
┌─────────────────────────┐
│  SmsController          │
└────────┬────────────────┘
         │
         ├──────────────────────┐
         │                      │
         ▼                      ▼
┌──────────────────┐   ┌──────────────────┐
│ SmsStorageService│   │ TelegramService  │
│                  │   │                  │
│ - Save to DB     │   │ - Format msg     │
│ - ReceivedAt=now │   │ - HTTPS POST     │
│ - SentToTG=false │   │ - api.telegram   │
└────────┬─────────┘   └──────────┬───────┘
         │                        │
         │                        │ Success?
         │                        │
         │              ┌─────────┴────────┐
         │              │                  │
         │              ▼ Yes              ▼ No
         │     ┌──────────────┐   ┌──────────────┐
         │     │MarkAsSent()  │   │ Stay unsent  │
         │     │ SentToTG=true│   │ Retry later  │
         │     └──────────────┘   └──────────────┘
         │
         ▼
┌─────────────────────────┐
│  Database (SQLite/PG)   │
│                         │
│  SmsMessages Table:     │
│  - Id (PK)              │
│  - Sender               │
│  - Text                 │
│  - Timestamp            │
│  - ReceivedAt           │
│  - SentToTelegram (bool)│
│  - SentToTelegramAt     │
└─────────────────────────┘
```

### 4. Background Retry

```
┌─────────────────────────────────────────┐
│  RetryService (Background Worker)       │
│                                         │
│  Every 5 minutes:                       │
│  1. Query unsent SMS (SentToTG=false)  │
│  2. For each SMS:                       │
│     - Try send to Telegram             │
│     - If success → MarkAsSent()        │
│     - If fail → keep unsent            │
│  3. Sleep 5 minutes                     │
└─────────────────────────────────────────┘
```

### 5. Server → Telegram

```
┌─────────────────────────┐
│  TelegramService        │
│                         │
│  Format:                │
│  📱 SMS от: +79...      │
│  🕐 Время: 14:30        │
│  📨 Текст: ...          │
└────────┬────────────────┘
         │
         │ HTTPS POST
         │ https://api.telegram.org/bot<TOKEN>/sendMessage
         │ {
         │   "chat_id": "123456",
         │   "text": "📱 SMS от: ..."
         │ }
         ▼
┌─────────────────────────┐
│  Telegram Bot API       │
│  - Receives message     │
│  - Delivers to chat     │
│  - Returns 200 OK       │
└─────────────────────────┘
```

## Error Handling

### Scenario 1: Server Unreachable

```
Device → Server (timeout/connection refused)
         │
         ├─ HTTP error
         │
         ▼
    SMS stays on SIM
         │
         ├─ Retry in 10 seconds
         │
         ▼
    Device checks again
```

### Scenario 2: Server Returns 500

```
Device → Server (500 Internal Server Error)
         │
         ├─ SMS saved to DB (SentToTG=false)
         │
         ├─ Telegram send failed
         │
         ▼
    Device: SMS stays on SIM
    Server: RetryService will retry in 5 min
```

### Scenario 3: Telegram API Down

```
Server → Telegram (timeout/error)
         │
         ├─ Exception in TelegramService
         │
         ├─ SMS remains in DB (SentToTG=false)
         │
         ▼
    RetryService retries every 5 minutes
```

### Scenario 4: Success Path

```
Device → Server → Telegram → Success
         │         │         │
         │         │         ├─ SentToTG=true
         │         │         ├─ SentToTelegramAt=now
         │         │         │
         │         └─────────┴─ 200 OK
         │
         ├─ Device receives 200 OK
         │
         ▼
    Device deletes SMS (AT+CMGD)
```

## Network Communication

### Device → Server

- **Protocol**: HTTP (no TLS)
- **Port**: 80 (configurable)
- **Method**: POST
- **Path**: `/api/sms`
- **Content-Type**: `application/json`
- **Timeout**: 30 seconds

### Server → Telegram

- **Protocol**: HTTPS (TLS 1.2+)
- **Port**: 443
- **Method**: POST
- **Path**: `/bot<TOKEN>/sendMessage`
- **Content-Type**: `application/json`

## Performance Characteristics

### Device (SIM-Relay)

- **SMS Check Interval**: 10 seconds
- **Network Check Interval**: 60 seconds
- **HTTP Timeout**: 30 seconds
- **Memory Usage**: ~50KB (ESP32)
- **Power Consumption**: ~200mA active, ~10mA idle

### Server (SIM-Orchestrator)

- **Request Handling**: Async/await (non-blocking)
- **Database**: Connection pooling (EF Core)
- **Retry Interval**: 5 minutes
- **Concurrency**: Thread-safe (ASP.NET Core)
- **Scalability**: Horizontal (stateless API)

## Security Model

### Device Layer

- ✅ No sensitive data stored (except APN)
- ✅ No TLS complexity (delegated to server)
- ✅ Minimal attack surface
- ⚠️ HTTP only (LAN/VPN recommended)

### Server Layer

- ✅ HTTPS for Telegram (TLS certificates)
- ✅ Database encryption at rest (optional)
- ✅ API authentication (recommended: API keys)
- ✅ Rate limiting (recommended)
- ✅ Input validation
- ✅ Structured logging (no PII in logs)

## Scalability

### Vertical Scaling

- Server can handle thousands of requests/sec
- Database indexes on `SentToTelegram`
- Connection pooling

### Horizontal Scaling

- Stateless API (load balancer friendly)
- Shared database (all instances)
- Retry service: leader election or distributed locks

## Monitoring Points

### Device Metrics

- SMS received count
- HTTP success/failure rate
- Network reconnect frequency
- SIM memory usage

### Server Metrics

- HTTP requests/sec
- SMS processing time (p50, p95, p99)
- Telegram API success rate
- Database query latency
- Unsent SMS count (alert if > threshold)

## Future Enhancements

### Device

1. **Deep Sleep**: Reduce power consumption
2. **SD Card Buffering**: Offline SMS storage
3. **OTA Updates**: Remote firmware updates
4. **Multiple SIM**: Failover between carriers

### Server

1. **Message Queue**: RabbitMQ/Redis for buffering
2. **Multi-tenancy**: Support multiple devices
3. **Web Dashboard**: Real-time SMS monitoring
4. **Webhook Support**: Forward to custom endpoints
5. **SMS Filtering**: Regex-based rules
6. **Analytics**: Sender statistics, volume trends

## Technology Stack

### Device (SIM-Relay)

| Component | Technology |
|-----------|------------|
| Platform | ESP32 (Espressif) |
| Framework | Arduino / ESP-IDF |
| Build System | PlatformIO |
| Modem Library | TinyGSM (fork) |
| HTTP Client | ArduinoHttpClient |
| JSON | ArduinoJson |

### Server (SIM-Orchestrator)

| Component | Technology |
|-----------|------------|
| Runtime | .NET 8.0 |
| Framework | ASP.NET Core Web API |
| ORM | Entity Framework Core |
| Database | SQLite / PostgreSQL / SQL Server |
| HTTP Client | HttpClient (built-in) |
| JSON | System.Text.Json |
| Logging | ILogger (built-in) |

## Deployment Topologies

### Development

```
┌──────────────┐      ┌──────────────┐
│  Device      │──────▶│  Localhost   │
│  (USB)       │ HTTP  │  :5000       │
└──────────────┘       └──────────────┘
```

### Production (Local Server)

```
┌──────────────┐      ┌──────────────┐
│  Device      │──────▶│  Server      │
│  (Battery)   │ HTTP  │  Ubuntu      │
│              │       │  systemd     │
└──────────────┘       └──────────────┘
         │                    │
         │ GPRS               │ Internet
         ▼                    ▼
    Cellular             Telegram API
```

### Production (Cloud)

```
┌──────────────┐      ┌──────────────┐
│  Device      │──────▶│  Reverse     │
│  (Battery)   │ HTTP  │  Proxy       │
│              │       │  (nginx)     │
└──────────────┘       └──────┬───────┘
         │                    │
         │                    ▼
         │             ┌──────────────┐
         │             │  Docker      │
         │             │  Container   │
         │             └──────┬───────┘
         │                    │
         │                    ▼
         │             ┌──────────────┐
         │             │  PostgreSQL  │
         │             └──────────────┘
         ▼
    Cellular Network
```

## Conclusion

The SIM Gateway system is designed with clear separation of concerns:

- **Device** (SIM-Relay): Simple, reliable SMS forwarding
- **Server** (SIM-Orchestrator): Complex logic, persistence, integrations

This architecture ensures:
- ✅ Easy debugging (clear boundaries)
- ✅ Maintainability (minimal device code)
- ✅ Reliability (retry mechanisms)
- ✅ Scalability (stateless server)
- ✅ Security (HTTPS on server side)
