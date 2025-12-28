# SIM-Orchestrator

Backend API server for the SMS Gateway system. Receives SMS from SIM-Relay devices, stores in database, forwards to Telegram.

## Architecture

```
SIM-Relay (ESP32)
      |  HTTPS POST + X-API-Key
      v
SIM-Orchestrator (this server)
      |--- Database (SQLite/PostgreSQL)
      +--- Telegram Bot API
```

## Quick Start

### 1. Prerequisites

- .NET 8.0 SDK
- Database: SQLite (dev) / PostgreSQL (prod)
- Telegram Bot token and Chat ID

### 2. Configuration

Copy `appsettings.json.example` to `appsettings.json`:

```json
{
  "ApiKey": "your-secret-api-key-min-32-characters-long",
  "TelegramBot": {
    "Token": "123456:ABC-DEF...",
    "ChatId": "your-chat-id"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=sms_gateway.db"
  }
}
```

**Important**: `ApiKey` must match `API_KEY` in SIM-Relay's `secrets.h`.

### 3. Run

```bash
cd src/SIM-Orchestrator
dotnet ef database update
dotnet run
```

Server starts on `https://localhost:5001`.

## API

### POST /api/sms

Receives SMS from device.

```bash
curl -X POST https://your-server.com/api/sms \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-api-key" \
  -d '{"sender": "+79991234567", "text": "Hello", "timestamp": "2025-12-28 14:30:00"}'
```

**Responses:**
- `200 OK` — SMS received and saved
- `401 Unauthorized` — Missing X-API-Key
- `403 Forbidden` — Invalid API key

### GET /api/health

Health check endpoint.

## Project Structure

```
src/SIM-Orchestrator/
├── Controllers/
│   ├── SmsController.cs      # POST /api/sms
│   └── HealthController.cs   # GET /api/health
├── Services/
│   ├── SmsStorageService.cs  # Database operations
│   ├── TelegramService.cs    # Telegram Bot API
│   └── RetryService.cs       # Background retry worker
├── Middleware/
│   └── ApiKeyMiddleware.cs   # X-API-Key validation
├── Models/
│   └── SmsMessage.cs         # Data model
└── Data/
    └── AppDbContext.cs       # EF Core context
```

## Deployment

### systemd (Ubuntu)

```bash
dotnet publish -c Release -o /var/www/sim-orchestrator
```

Create `/etc/systemd/system/sim-orchestrator.service`:

```ini
[Unit]
Description=SIM Orchestrator API
After=network.target

[Service]
Type=notify
WorkingDirectory=/var/www/sim-orchestrator
ExecStart=/usr/bin/dotnet SIM-Orchestrator.dll
Restart=always
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now sim-orchestrator
```

### Docker

```bash
docker build -t sim-orchestrator .
docker run -p 5000:80 \
  -e ApiKey="your-api-key" \
  -e TelegramBot__Token="your-bot-token" \
  -e TelegramBot__ChatId="your-chat-id" \
  sim-orchestrator
```

## Telegram Setup

1. Message [@BotFather](https://t.me/BotFather) → `/newbot`
2. Copy token (format: `123456:ABC-DEF1234...`)
3. Get Chat ID from [@userinfobot](https://t.me/userinfobot)

## Troubleshooting

| Problem | Solution |
|---------|----------|
| 401/403 errors | Check X-API-Key matches between device and server |
| Telegram fails | Verify bot token and chat ID; check firewall (port 443) |
| Database errors | Run `dotnet ef database update` |
| Connection refused | Check server is running and port is open |

## Related

- **SIM-Relay**: ESP32 firmware that sends SMS to this server
