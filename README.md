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

## Quick Start (Docker) — Recommended

### 1. Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Windows/Mac) or Docker Engine (Linux)
- Telegram Bot token and Chat ID (see [Telegram Setup](#telegram-setup))

### 2. Configuration

Create `.env` file in project root:

```bash
API_KEY=your-secret-api-key-min-32-characters-long
TELEGRAM_BOT_TOKEN=123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11
TELEGRAM_CHAT_ID=123456789
```

**Important**: `API_KEY` must match `API_KEY` in SIM-Relay's `secrets.h`.

### 3. Run

```bash
docker-compose up -d
```

Server starts on `http://localhost:5000`.

### 4. Verify

```bash
curl http://localhost:5000/health
```

### 5. View logs

```bash
docker-compose logs -f
```

### 6. Stop

```bash
docker-compose down
```

---

## Alternative: Manual Run (without Docker)

### Prerequisites

- .NET 8.0 SDK
- Database: SQLite (dev) / PostgreSQL (prod)

### Configuration

Copy `appsettings.json.example` to `appsettings.json`:

```json
{
  "ApiKey": "your-secret-api-key-min-32-characters-long",
  "Telegram": {
    "BotToken": "123456:ABC-DEF...",
    "ChatId": "your-chat-id"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=sms_gateway.db"
  }
}
```

### Run

```bash
cd src/SIM-Orchestrator
dotnet ef database update
dotnet run
```

Server starts on `http://localhost:5000`.

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

### Docker (Production)

Using docker-compose (recommended):

```bash
# Create .env file with your settings
cat > .env << EOF
API_KEY=your-secret-api-key-min-32-characters-long
TELEGRAM_BOT_TOKEN=123456:ABC-DEF...
TELEGRAM_CHAT_ID=123456789
EOF

# Start the service
docker-compose up -d

# Check status
docker-compose ps
docker-compose logs -f
```

Or build and run manually:

```bash
docker build -t sim-orchestrator ./src/SIM-Orchestrator
docker run -d -p 5000:8080 \
  -e ApiKey="your-api-key" \
  -e Telegram__BotToken="your-bot-token" \
  -e Telegram__ChatId="your-chat-id" \
  -v sms-data:/data \
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
