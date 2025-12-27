using Microsoft.EntityFrameworkCore;
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

        modelBuilder.Entity<SmsMessage>()
            .HasIndex(s => s.SentToTelegram);

        modelBuilder.Entity<SmsMessage>()
            .HasIndex(s => s.ReceivedAt);
    }
}
