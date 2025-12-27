# SIM-Orchestrator

Backend API Server for SMS Gateway System

## Overview

SIM-Orchestrator is a C# ASP.NET Core Web API server that receives SMS messages from SIM-Relay devices via HTTP, stores them in a database, and forwards them to Telegram Bot API. It handles all complex logic, HTTPS/TLS, retry mechanisms, and persistence.

### Architecture

```
SIM-Relay Device
      ↓  HTTPS POST (JSON + X-API-Key)
SIM-Orchestrator (This Server)
      ├→ API Key Authentication
      ├→ Database (SQL Server/PostgreSQL/SQLite)
      └→ Telegram Bot API (HTTPS)
```

## Features

- **HTTPS API**: Secure TLS-encrypted endpoint for SMS reception
- **API Key Authentication**: X-API-Key header validation via middleware
- **Database Storage**: Persists all SMS with delivery status
- **Telegram Integration**: Sends SMS to Telegram bot/channel
- **Retry Mechanism**: Automatic retry for failed Telegram sends
- **Logging**: Comprehensive logging for monitoring
- **Cross-platform**: Runs on Ubuntu, Windows, Docker

## Requirements

- **.NET 8.0 SDK** or later
- **Database**: SQL Server / PostgreSQL / SQLite
- **Telegram Bot**: Bot token and chat ID
- **Ubuntu Server** (or any Linux/Windows with .NET)

## Project Structure

```
SIM-Orchestrator/
├── SIM-Orchestrator.sln
├── SIM-Orchestrator/
│   ├── Program.cs                 # Application entry point
│   ├── appsettings.json           # Configuration (use .example as template)
│   ├── Middleware/
│   │   └── ApiKeyMiddleware.cs    # X-API-Key authentication
│   ├── Controllers/
│   │   └── SmsController.cs       # POST /api/sms endpoint
│   ├── Services/
│   │   ├── ISmsStorageService.cs
│   │   ├── SmsStorageService.cs   # Database operations
│   │   ├── ITelegramService.cs
│   │   ├── TelegramService.cs     # Telegram Bot API
│   │   └── RetryService.cs        # Background retry worker
│   ├── Models/
│   │   └── SmsMessage.cs          # Data model
│   └── Data/
│       └── AppDbContext.cs        # Entity Framework context
└── README.md
```

## Quick Start

### 1. Prerequisites

Install .NET 8.0 SDK:

**Ubuntu**:
```bash
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0
```

**Windows**:
Download from https://dotnet.microsoft.com/download

### 2. Create Project

```bash
# Create solution
dotnet new sln -n SIM-Orchestrator

# Create Web API project
dotnet new webapi -n SIM-Orchestrator

# Add project to solution
dotnet sln add SIM-Orchestrator/SIM-Orchestrator.csproj

# Navigate to project
cd SIM-Orchestrator
```

### 3. Install Dependencies

```bash
# Entity Framework Core
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Design

# Database provider (choose one)
dotnet add package Microsoft.EntityFrameworkCore.SqlServer    # SQL Server
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL      # PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Sqlite       # SQLite

# HTTP client for Telegram
dotnet add package Microsoft.Extensions.Http
```

### 4. Configuration

Copy `appsettings.json.example` to `appsettings.json` and edit:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ApiKey": "your-secret-api-key-min-32-characters-long",
  "TelegramBot": {
    "Token": "YOUR_TELEGRAM_BOT_TOKEN",
    "ChatId": "YOUR_CHAT_ID"
  }
}
```

**Important**: The `ApiKey` must match the key configured in your SIM-Relay device's `secrets.h`.

#### Get Telegram Bot Token

1. Open Telegram and search for [@BotFather](https://t.me/BotFather)
2. Send `/newbot`
3. Follow instructions to create bot
4. Copy the token (format: `123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11`)

#### Get Chat ID

**For private chat** (messages to yourself):
1. Search for [@userinfobot](https://t.me/userinfobot) in Telegram
2. Start conversation
3. Bot will send your user ID

**For channel**:
1. Create a channel
2. Add your bot as administrator
3. Get channel ID (format: `@channelname` or `-100123456789`)

### 5. Database Setup

#### SQLite (Simplest, for development)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=sms_gateway.db"
  }
}
```

#### PostgreSQL (Recommended for production)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=sms_gateway;Username=postgres;Password=yourpassword"
  }
}
```

Install PostgreSQL:
```bash
sudo apt install postgresql postgresql-contrib
sudo -u postgres createdb sms_gateway
```

#### SQL Server

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=SmsGateway;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

### 6. Run Migrations

```bash
# Create initial migration
dotnet ef migrations add InitialCreate

# Apply migration to database
dotnet ef database update
```

### 7. Run Server

```bash
# Development
dotnet run

# Production
dotnet run --configuration Release
```

Server will start on `http://localhost:5000` (HTTP) and `https://localhost:5001` (HTTPS).

## Implementation Guide

### Step 1: Create Models

**Models/SmsMessage.cs**:
```csharp
using System;
using System.ComponentModel.DataAnnotations;

namespace SIM_Orchestrator.Models
{
    public class SmsMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Sender { get; set; }

        [Required]
        public string Text { get; set; }

        [MaxLength(50)]
        public string Timestamp { get; set; }

        public DateTime ReceivedAt { get; set; }

        public bool SentToTelegram { get; set; }

        public DateTime? SentToTelegramAt { get; set; }
    }
}
```

### Step 2: Create Database Context

**Data/AppDbContext.cs**:
```csharp
using Microsoft.EntityFrameworkCore;
using SIM_Orchestrator.Models;

namespace SIM_Orchestrator.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<SmsMessage> SmsMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Create index on SentToTelegram for retry queries
            modelBuilder.Entity<SmsMessage>()
                .HasIndex(s => s.SentToTelegram);
        }
    }
}
```

### Step 3: Create Services

**Services/ISmsStorageService.cs**:
```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using SIM_Orchestrator.Models;

namespace SIM_Orchestrator.Services
{
    public interface ISmsStorageService
    {
        Task<SmsMessage> SaveSmsAsync(SmsMessage sms);
        Task<List<SmsMessage>> GetUnsentSmsAsync();
        Task MarkAsSentAsync(int smsId);
    }
}
```

**Services/SmsStorageService.cs**:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SIM_Orchestrator.Data;
using SIM_Orchestrator.Models;

namespace SIM_Orchestrator.Services
{
    public class SmsStorageService : ISmsStorageService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SmsStorageService> _logger;

        public SmsStorageService(AppDbContext context, ILogger<SmsStorageService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<SmsMessage> SaveSmsAsync(SmsMessage sms)
        {
            sms.ReceivedAt = DateTime.UtcNow;
            sms.SentToTelegram = false;

            _context.SmsMessages.Add(sms);
            await _context.SaveChangesAsync();

            _logger.LogInformation("SMS saved to database. ID: {Id}, From: {Sender}",
                sms.Id, sms.Sender);

            return sms;
        }

        public async Task<List<SmsMessage>> GetUnsentSmsAsync()
        {
            return await _context.SmsMessages
                .Where(s => !s.SentToTelegram)
                .OrderBy(s => s.ReceivedAt)
                .ToListAsync();
        }

        public async Task MarkAsSentAsync(int smsId)
        {
            var sms = await _context.SmsMessages.FindAsync(smsId);
            if (sms != null)
            {
                sms.SentToTelegram = true;
                sms.SentToTelegramAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("SMS marked as sent. ID: {Id}", smsId);
            }
        }
    }
}
```

**Services/ITelegramService.cs**:
```csharp
using System.Threading.Tasks;
using SIM_Orchestrator.Models;

namespace SIM_Orchestrator.Services
{
    public interface ITelegramService
    {
        Task SendSmsToTelegramAsync(SmsMessage sms);
    }
}
```

**Services/TelegramService.cs**:
```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SIM_Orchestrator.Models;

namespace SIM_Orchestrator.Services
{
    public class TelegramService : ITelegramService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<TelegramService> _logger;
        private readonly string _botToken;
        private readonly string _chatId;

        public TelegramService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<TelegramService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _botToken = configuration["Telegram:BotToken"];
            _chatId = configuration["Telegram:ChatId"];
        }

        public async Task SendSmsToTelegramAsync(SmsMessage sms)
        {
            // Format message
            var message = $"📱 SMS от: {sms.Sender}\n" +
                         $"🕐 Время: {sms.Timestamp}\n" +
                         $"📨 Текст: {sms.Text}";

            // Telegram API endpoint
            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";

            // Create payload
            var payload = new
            {
                chat_id = _chatId,
                text = message
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Send request
            _logger.LogInformation("Sending SMS to Telegram. SMS ID: {Id}", sms.Id);

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("SMS sent to Telegram successfully. SMS ID: {Id}", sms.Id);
        }
    }
}
```

**Services/RetryService.cs**:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SIM_Orchestrator.Services
{
    public class RetryService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RetryService> _logger;
        private readonly TimeSpan _retryInterval = TimeSpan.FromMinutes(5);

        public RetryService(
            IServiceProvider serviceProvider,
            ILogger<RetryService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Retry Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var storage = scope.ServiceProvider.GetRequiredService<ISmsStorageService>();
                    var telegram = scope.ServiceProvider.GetRequiredService<ITelegramService>();

                    var unsentSms = await storage.GetUnsentSmsAsync();

                    if (unsentSms.Count > 0)
                    {
                        _logger.LogInformation("Found {Count} unsent SMS, attempting retry", unsentSms.Count);

                        foreach (var sms in unsentSms)
                        {
                            try
                            {
                                await telegram.SendSmsToTelegramAsync(sms);
                                await storage.MarkAsSentAsync(sms.Id);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to retry SMS ID: {Id}", sms.Id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in retry service");
                }

                await Task.Delay(_retryInterval, stoppingToken);
            }
        }
    }
}
```

### Step 4: Create Controller

**Controllers/SmsController.cs**:
```csharp
using Microsoft.AspNetCore.Mvc;
using SIM_Orchestrator.Models;
using SIM_Orchestrator.Services;

namespace SIM_Orchestrator.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SmsController : ControllerBase
    {
        private readonly ISmsStorageService _storage;
        private readonly ITelegramService _telegram;
        private readonly ILogger<SmsController> _logger;

        public SmsController(
            ISmsStorageService storage,
            ITelegramService telegram,
            ILogger<SmsController> logger)
        {
            _storage = storage;
            _telegram = telegram;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> ReceiveSms([FromBody] SmsMessage sms)
        {
            try
            {
                _logger.LogInformation("Received SMS from {Sender}", sms.Sender);

                // Save to database
                var savedSms = await _storage.SaveSmsAsync(sms);

                // Send to Telegram (async, but wait for result)
                try
                {
                    await _telegram.SendSmsToTelegramAsync(savedSms);
                    await _storage.MarkAsSentAsync(savedSms.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send SMS to Telegram, will retry later");
                    // Don't fail the request - retry service will handle it
                }

                return Ok(new { status = "received", id = savedSms.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SMS");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
```

### Step 5: Configure Program.cs

**Program.cs**:
```csharp
using Microsoft.EntityFrameworkCore;
using SIM_Orchestrator.Data;
using SIM_Orchestrator.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database (SQLite example)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// HTTP Client for Telegram
builder.Services.AddHttpClient<ITelegramService, TelegramService>();

// Custom services
builder.Services.AddScoped<ISmsStorageService, SmsStorageService>();

// Background service for retry
builder.Services.AddHostedService<RetryService>();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
```

## Deployment

### Ubuntu Server (systemd)

1. **Publish application**:
```bash
dotnet publish -c Release -o /var/www/sim-orchestrator
```

2. **Create systemd service** (`/etc/systemd/system/sim-orchestrator.service`):
```ini
[Unit]
Description=SIM Orchestrator API
After=network.target

[Service]
Type=notify
WorkingDirectory=/var/www/sim-orchestrator
ExecStart=/usr/bin/dotnet /var/www/sim-orchestrator/SIM-Orchestrator.dll
Restart=always
RestartSec=10
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

3. **Start service**:
```bash
sudo systemctl daemon-reload
sudo systemctl enable sim-orchestrator
sudo systemctl start sim-orchestrator
sudo systemctl status sim-orchestrator
```

4. **Configure reverse proxy** (nginx):
```nginx
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
    }
}
```

### Docker

**Dockerfile**:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["SIM-Orchestrator/SIM-Orchestrator.csproj", "SIM-Orchestrator/"]
RUN dotnet restore "SIM-Orchestrator/SIM-Orchestrator.csproj"
COPY . .
WORKDIR "/src/SIM-Orchestrator"
RUN dotnet build "SIM-Orchestrator.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SIM-Orchestrator.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SIM-Orchestrator.dll"]
```

**docker-compose.yml**:
```yaml
version: '3.8'
services:
  api:
    build: .
    ports:
      - "5000:80"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__DefaultConnection=Data Source=/data/sms_gateway.db
      - Telegram__BotToken=YOUR_BOT_TOKEN
      - Telegram__ChatId=YOUR_CHAT_ID
    volumes:
      - ./data:/data
```

## Testing

### Test with curl

```bash
curl -X POST https://localhost:5001/api/sms \
  -H "Content-Type: application/json" \
  -H "X-API-Key: your-secret-api-key-min-32-characters-long" \
  -d '{
    "sender": "+79991234567",
    "text": "Тестовое сообщение",
    "timestamp": "2025-12-26 14:30:15"
  }'
```

Expected response:
```json
{
  "status": "received",
  "id": 1
}
```

Error responses:
- `401 Unauthorized` - Missing X-API-Key header
- `403 Forbidden` - Invalid API key

### Check logs

```bash
# Systemd
sudo journalctl -u sim-orchestrator -f

# Docker
docker logs -f <container-id>
```

## Monitoring

### Database Queries

```sql
-- Count total SMS
SELECT COUNT(*) FROM SmsMessages;

-- Count unsent SMS
SELECT COUNT(*) FROM SmsMessages WHERE SentToTelegram = 0;

-- Recent SMS
SELECT * FROM SmsMessages ORDER BY ReceivedAt DESC LIMIT 10;

-- Failed sends (older than 1 hour, still unsent)
SELECT * FROM SmsMessages
WHERE SentToTelegram = 0
  AND ReceivedAt < datetime('now', '-1 hour');
```

### Health Check Endpoint

Add to `SmsController.cs`:

```csharp
[HttpGet("health")]
public IActionResult Health()
{
    return Ok(new {
        status = "healthy",
        timestamp = DateTime.UtcNow
    });
}
```

Test:
```bash
curl https://localhost:5001/api/sms/health \
  -H "X-API-Key: your-secret-api-key"
```

## Troubleshooting

### Telegram sends fail

1. Check bot token is correct
2. Verify chat ID format
3. For channels, ensure bot is administrator
4. Check firewall allows outbound HTTPS (443)

### Database errors

1. Verify connection string
2. Check database exists
3. Run migrations: `dotnet ef database update`
4. Check file permissions (SQLite)

### SIM-Relay can't connect

1. Verify server is running and API key is valid
2. Check firewall rules (open port 443 for HTTPS)
3. Verify SSL certificate is valid
4. Ensure API key in device `secrets.h` matches server `appsettings.json`
5. Test with curl from device's network:
   ```bash
   curl -X POST https://your-server.com/api/sms \
     -H "X-API-Key: your-api-key" \
     -H "Content-Type: application/json" \
     -d '{"sender":"test","text":"test","timestamp":"test"}'
   ```

## Security

### Implemented

- [x] **API Key Authentication**: X-API-Key header validation via `ApiKeyMiddleware`
- [x] **HTTPS Support**: TLS encryption for all API endpoints

### Production Checklist

- [x] API Key authentication (implemented)
- [ ] Use HTTPS with valid SSL certificate (Let's Encrypt)
- [ ] Rate limiting for `/api/sms` endpoint
- [ ] Restrict allowed IPs (if device has static IP)
- [ ] Use environment variables for secrets
- [ ] Enable CORS only for trusted origins
- [ ] Regular database backups

### Environment Variables

Instead of `appsettings.json`, use environment variables for secrets:

```bash
export ApiKey="your-secret-api-key-min-32-characters-long"
export ConnectionStrings__DefaultConnection="..."
export TelegramBot__Token="..."
export TelegramBot__ChatId="..."
dotnet run
```

## License

MIT License

## Related Projects

- **SIM-Relay**: ESP32 device firmware that sends SMS to this server

## Support

For issues and questions, please open an issue on GitHub.
