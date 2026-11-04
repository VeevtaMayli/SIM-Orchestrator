using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SIMOrchestrator.Data;
using SIMOrchestrator.Services;

namespace SIMOrchestrator.Tests.TestFixtures;

/// <summary>
/// Boots the real application against an in-memory SQLite database (so migrations,
/// indexes and constraints are exercised), a fake Telegram service and a capturing logger.
/// The database is a named shared-cache in-memory database: every DbContext opens its
/// own connection (as in production) and the fixture keeps one connection open so the
/// database lives for the duration of the test class.
/// </summary>
public class SimOrchestratorFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "integration-test-api-key-0123456789abcdef";
    public const string BotToken = "123456789:integration-test-bot-token";

    private SqliteConnection? _connection;

    public FakeTelegramService Telegram { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>Configuration values applied on top of appsettings; edit before the first client is created.</summary>
    public Dictionary<string, string?> Settings { get; } = new()
    {
        ["ApiKey"] = ApiKey,
        ["Telegram:BotToken"] = BotToken,
        ["Telegram:ChatId"] = "42",
        ["ConnectionStrings:DefaultConnection"] = "Data Source=overridden-by-fixture",
        // Tests post bursts of messages; do not pace deliveries one second apart.
        ["Delivery:DelayBetweenMessages"] = "00:00:00"
    };

    /// <summary>Extra service registrations applied after the fixture's own overrides.</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; set; }

    public string Environment { get; set; } = "Development";

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(Settings));

        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(Logs);
            logging.SetMinimumLevel(LogLevel.Trace);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();

            var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

            services.RemoveAll<ITelegramService>();
            services.AddSingleton<ITelegramService>(Telegram);

            // TestServer has no remote address, so trust X-Forwarded-For from anywhere.
            services.PostConfigure<ForwardedHeadersOptions>(options =>
            {
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            ConfigureTestServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection?.Dispose();
    }
}
