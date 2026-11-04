using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SIMOrchestrator.Data;
using SIMOrchestrator.HealthChecks;
using SIMOrchestrator.Middleware;
using SIMOrchestrator.Options;
using SIMOrchestrator.Services;

var builder = WebApplication.CreateBuilder(args);

// No-op outside systemd; under systemd makes Type=notify work and routes logs to the journal.
builder.Host.UseSystemd();

// An SMS payload is a few kilobytes at most; anything larger is not a device.
const long maxRequestBodyBytes = 64 * 1024;
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = maxRequestBodyBytes);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// The app runs behind a reverse proxy (Caddy) on the same host: trust the
// loopback proxy (the default KnownProxies/KnownNetworks) for the client IP
// and scheme so that rate limiting sees the real client address.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Rate limiting: 60 requests per minute per client IP
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("fixed", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddTelegramClient();

builder.Services.AddApiKeyAuthentication();
if (builder.Environment.IsDevelopment())
{
    builder.Services.PostConfigure<ApiKeyOptions>(options =>
        options.AnonymousPathPrefixes = [.. options.AnonymousPathPrefixes, "/swagger"]);
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISmsStorageService, SmsStorageService>();

// Outbox: requests only store and signal; the dispatcher delivers with retries.
builder.Services.AddOptions<DeliveryOptions>()
    .BindConfiguration(DeliveryOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IDeliveryQueue, DeliveryQueue>();
builder.Services.AddHostedService<DeliveryDispatcher>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(HealthResponseWriter.DatabaseCheckName)
    .AddCheck<SmsBacklogHealthCheck>(SmsBacklogHealthCheck.Name);

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();

app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.MapControllers().RequireRateLimiting("fixed");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();

public partial class Program { }
