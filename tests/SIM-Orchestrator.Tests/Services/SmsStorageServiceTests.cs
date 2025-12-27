using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SIMOrchestrator.Data;
using SIMOrchestrator.Models;
using SIMOrchestrator.Services;
using Xunit;

namespace SIMOrchestrator.Tests.Services;

public class SmsStorageServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly SmsStorageService _service;

    public SmsStorageServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        var loggerMock = new Mock<ILogger<SmsStorageService>>();
        _service = new SmsStorageService(_context, loggerMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task SaveSmsAsync_ValidRequest_SavesAndReturnsMessage()
    {
        var request = new SmsRequest { Sender = "+1234567890", Text = "Test" };

        var result = await _service.SaveSmsAsync(request);

        Assert.NotEqual(0, result.Id);
        Assert.Equal(request.Sender, result.Sender);
        Assert.False(result.SentToTelegram);
        Assert.Equal(1, await _context.SmsMessages.CountAsync());
    }

    [Fact]
    public async Task GetUnsentSmsAsync_ReturnsOnlyUnsent()
    {
        _context.SmsMessages.AddRange(
            new SmsMessage { Sender = "1", Text = "Unsent", SentToTelegram = false },
            new SmsMessage { Sender = "2", Text = "Sent", SentToTelegram = true }
        );
        await _context.SaveChangesAsync();

        var unsent = await _service.GetUnsentSmsAsync();

        Assert.Single(unsent);
        Assert.Equal("1", unsent[0].Sender);
    }

    [Fact]
    public async Task MarkAsSentAsync_UpdatesMessage()
    {
        var sms = new SmsMessage { Sender = "1", Text = "Test", SentToTelegram = false };
        _context.SmsMessages.Add(sms);
        await _context.SaveChangesAsync();

        await _service.MarkAsSentAsync(sms.Id);

        var updated = await _context.SmsMessages.FindAsync(sms.Id);
        Assert.True(updated!.SentToTelegram);
        Assert.NotNull(updated.SentToTelegramAt);
    }
}
