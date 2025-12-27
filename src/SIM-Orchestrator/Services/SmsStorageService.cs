using Microsoft.EntityFrameworkCore;
using SIMOrchestrator.Data;
using SIMOrchestrator.Models;

namespace SIMOrchestrator.Services;

public class SmsStorageService : ISmsStorageService
{
    private readonly AppDbContext _context;
    private readonly ILogger<SmsStorageService> _logger;

    public SmsStorageService(AppDbContext context, ILogger<SmsStorageService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<SmsMessage> SaveSmsAsync(SmsRequest request)
    {
        var sms = new SmsMessage
        {
            Sender = request.Sender,
            Text = request.Text,
            Timestamp = request.Timestamp,
            ReceivedAt = DateTime.UtcNow,
            SentToTelegram = false
        };

        _context.SmsMessages.Add(sms);
        await _context.SaveChangesAsync();

        _logger.LogInformation("SMS saved. ID: {Id}, From: {Sender}", sms.Id, sms.Sender);
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
