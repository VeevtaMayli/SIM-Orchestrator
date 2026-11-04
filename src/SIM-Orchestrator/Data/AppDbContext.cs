using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SIMOrchestrator.Models;

namespace SIMOrchestrator.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<SmsMessage> SmsMessages => Set<SmsMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var sms = modelBuilder.Entity<SmsMessage>();
        sms.HasIndex(s => s.SentToTelegram);
        sms.HasIndex(s => s.ReceivedAt);
        sms.HasIndex(s => s.DedupKey).IsUnique();
        sms.HasIndex(s => new { s.Status, s.NextAttemptAt });

        ApplyUtcDateTimeConvention(modelBuilder);
    }

    /// <summary>
    /// Every DateTime in this model is UTC. SQLite has no notion of time zones and
    /// hands values back with Kind=Unspecified, so mark them UTC on the way out and
    /// normalise any local value to UTC on the way in.
    /// </summary>
    private static void ApplyUtcDateTimeConvention(ModelBuilder modelBuilder)
    {
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableUtc = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(utc);
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(nullableUtc);
            }
        }
    }
}
