# SIM-Orchestrator

Backend API server for the SMS Gateway system. Receives SMS from SIM-Relay devices, stores in database, forwards to Telegram.

> **Runtime note:** the project targets .NET 8 (LTS), whose Microsoft support ends on 10 November 2026. Moving to .NET 10 LTS is the next planned step.

## Architecture

```
SIM-Relay (ESP32)
      |  HTTPS POST + X-API-Key
      v
SIM-Orchestrator (this server)
      |--- Database (SQLite)
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
- Database: SQLite (the file is created and migrated automatically on first start)

### Configuration

Secrets are not stored in `appsettings.json`. Use environment variables or
[user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):

```bash
cd src/SIM-Orchestrator
dotnet user-secrets init
dotnet user-secrets set "ApiKey" "your-secret-api-key-min-32-characters-long"
dotnet user-secrets set "Telegram:BotToken" "123456:ABC-DEF..."
dotnet user-secrets set "Telegram:ChatId" "your-chat-id"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Data Source=sms_gateway.db"
```

The server validates this configuration on startup and refuses to start if anything is missing.

### Run

```bash
cd src/SIM-Orchestrator
dotnet run
```

Database migrations are applied automatically on startup; `dotnet ef database update` is not needed.

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

The SMS is stored and the request returns immediately; delivery to Telegram happens in
the background with retries. An optional `messageId` (up to 64 characters, assigned by
the device) makes retries idempotent. Without it the server derives a key from
`sender`, `timestamp` and `text`, so re-sending the same SMS after a lost response
never creates a second copy.

**Responses:**
- `200 OK` `{"status":"received","id":123}` — SMS stored, delivery queued
- `200 OK` `{"status":"duplicate","id":123}` — this SMS was already stored; nothing new is sent
- `400 Bad Request` — Validation failed (`sender` and `text` are required, `text` is at most 4000 characters)
- `401 Unauthorized` — Missing or invalid `X-API-Key`
- `429 Too Many Requests` — More than 60 requests per minute from one client IP

Request bodies larger than 64 KB are rejected.

### GET /health

Health check endpoint, no API key required. Returns `200` with
`{"status":"healthy","database":"connected","unsentMessages":0,"failedMessages":0,...}`,
`200` with `"status":"degraded"` when some messages failed permanently (see below), or
`503` when a check fails. The response never contains internal error details.

### Delivery and retries

Every stored SMS goes through `Pending → InProgress → Sent`. A failed Telegram call puts
the message back to `Pending` with a growing delay (1, 5, 15, then 60 minutes); a `429`
from Telegram is honoured via its `retry_after`. After `MaxAttempts` the message becomes
`Failed`, is reported in `/health` as `failedMessages` and is not retried automatically.
Texts that do not fit one Telegram message are sent in numbered parts.

Defaults can be changed with environment variables:

```bash
Delivery__PollInterval=00:01:00        # how often to look for due messages
Delivery__BatchSize=50
Delivery__MaxAttempts=20
Delivery__LeaseDuration=00:02:00       # how long a claimed message is reserved
Delivery__DelayBetweenMessages=00:00:01
Delivery__BackoffMinutes__0=1          # ... __1=5, __2=15, __3=60
```

### Message format and time zone

Telegram messages show the SMS text (digit runs of 4+ in monospace for easy copying),
the sender, the time the SMS was sent (`🕒`) and the time the server received it (`📥`).
Both times are rendered in `Display__TimeZone` (IANA id, default `UTC`), for example:

```bash
DISPLAY_TIMEZONE=Europe/Moscow
```

The device `timestamp` is accepted as ISO 8601 (`2025-12-28T14:30:00+03:00`), as the
modem's `yy/MM/dd,HH:mm:ss±zz` (zz in quarter hours) or as plain `yyyy-MM-dd HH:mm:ss`.
A timestamp without a zone is shown exactly as sent; an unparsable one is shown verbatim.

### API key rotation

Several keys can be active at once, so a key can be rotated without downtime:

```bash
API_KEY=current-key-min-32-characters-long
API_KEY_NEXT=new-key-min-32-characters-long   # optional, also accepted while devices migrate
```

Every key must be at least 32 characters; the server refuses to start otherwise.
When running without Docker, use `ApiKeys__0`, `ApiKeys__1`, ... environment variables
(or the `ApiKeys` array in configuration).

## Project Structure

```
src/SIM-Orchestrator/
├── Controllers/
│   └── SmsController.cs      # POST /api/sms
├── HealthChecks/             # GET /health (database + delivery backlog)
├── Services/
│   ├── SmsStorageService.cs  # Database operations
│   ├── TelegramService.cs    # Telegram Bot API
│   └── DeliveryDispatcher.cs # Outbox worker: retries, backoff, failure handling
├── Middleware/
│   └── ApiKeyMiddleware.cs   # X-API-Key validation
├── Options/                  # Validated configuration (ApiKey(s), Telegram)
├── Models/
│   └── SmsMessage.cs         # Data model
└── Data/
    └── AppDbContext.cs       # EF Core context
```

## Deployment

### Production with HTTPS (Caddy)

**Prerequisites:**
- VPS with Ubuntu 24.04
- Docker and docker-compose installed
- Ports 80, 443 open in firewall

**1. Install Caddy:**

```bash
# Download and run setup script (the domain must already resolve to this server)
wget https://raw.githubusercontent.com/VeevtaMayli/SIM-Orchestrator/main/setup-caddy.sh
chmod +x setup-caddy.sh
./setup-caddy.sh sms.example.com
```

Or manually:

```bash
# Install Caddy
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update
sudo apt install -y caddy

# Create Caddyfile
sudo nano /etc/caddy/Caddyfile
```

Paste:

```
sms.example.com {
    reverse_proxy localhost:5000

    header {
        Strict-Transport-Security "max-age=31536000; includeSubDomains; preload"
        X-Frame-Options "DENY"
        X-Content-Type-Options "nosniff"
        Referrer-Policy "no-referrer"
    }

    log {
        output file /var/log/caddy/sim-orchestrator.log
        format json
    }
}
```

**2. Start services:**

```bash
# Open firewall
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Start Caddy
sudo systemctl enable caddy
sudo systemctl restart caddy

# Deploy SIM-Orchestrator
cd ~/sim-orchestrator
docker compose up -d
```

**3. Verify HTTPS:**

```bash
curl -I https://sms.example.com/health
```

**4. Automatic deployment from CI:** on every push to `main` the workflow builds the image,
pushes it to GHCR and runs `docker compose pull && docker compose up -d` on the VPS over SSH
(secrets `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY`). For `docker compose pull` to work,
the `.env` on the VPS must point at the registry image:

```bash
DOCKER_IMAGE=ghcr.io/veevtamayli/sim-orchestrator:latest
```

Caddy automatically provisions Let's Encrypt SSL certificates.

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
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
EnvironmentFile=/etc/sim-orchestrator.env

[Install]
WantedBy=multi-user.target
```

`/etc/sim-orchestrator.env` (readable by root only) holds the same settings as the Docker `.env`
in configuration form, for example `ApiKey=...`, `Telegram__BotToken=...`, `Telegram__ChatId=...`,
`ConnectionStrings__DefaultConnection=Data Source=/var/lib/sim-orchestrator/sms_gateway.db`.

```bash
sudo chmod 600 /etc/sim-orchestrator.env
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
| 401 errors | Check X-API-Key matches between device and server |
| Telegram fails | Verify bot token and chat ID; check firewall (port 443); `/health` shows `unsentMessages`/`failedMessages` |
| Database errors | Check the volume/path is writable; migrations run automatically on startup |
| Connection refused | Check server is running and port is open |

## Related

- **SIM-Relay**: ESP32 firmware that sends SMS to this server
