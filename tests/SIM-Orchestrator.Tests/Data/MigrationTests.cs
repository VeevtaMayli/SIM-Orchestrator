using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SIMOrchestrator.Data;
using SIMOrchestrator.Models;
using Xunit;

namespace SIMOrchestrator.Tests.Data;

public class MigrationTests
{
    [Fact]
    public async Task AddDeliveryTracking_UpgradesPopulatedLegacyDatabase()
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;

        // Schema as it was before this change, with one delivered and two pending rows.
        await using (var legacy = new AppDbContext(options))
        {
            await legacy.GetService<IMigrator>().MigrateAsync("InitialCreate");
            await legacy.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "SmsMessages" ("Sender", "Text", "Timestamp", "ReceivedAt", "SentToTelegram", "SentToTelegramAt")
                VALUES ('+1', 'sent', NULL, '2025-12-01 10:00:00', 1, '2025-12-01 10:00:05'),
                       ('+2', 'pending-a', NULL, '2025-12-01 11:00:00', 0, NULL),
                       ('+3', 'pending-b', NULL, '2025-12-01 12:00:00', 0, NULL);
                """);
        }

        await using var upgraded = new AppDbContext(options);
        await upgraded.Database.MigrateAsync();

        var rows = await upgraded.SmsMessages.OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(DeliveryStatus.Sent, rows[0].Status);
        Assert.Equal(1, rows[0].Attempts);
        Assert.Equal(DeliveryStatus.Pending, rows[1].Status);
        Assert.Equal(DeliveryStatus.Pending, rows[2].Status);
        Assert.Equal(rows.Count, rows.Select(r => r.DedupKey).Distinct().Count());
        Assert.All(rows, r => Assert.StartsWith("legacy:", r.DedupKey));
        Assert.All(rows, r => Assert.Equal(DateTimeKind.Utc, r.ReceivedAt.Kind));

        // The unique index is in place: a second row with an existing key is rejected.
        upgraded.SmsMessages.Add(new SmsMessage { Sender = "+4", Text = "dup", DedupKey = rows[0].DedupKey, ReceivedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => upgraded.SaveChangesAsync());
    }
}
