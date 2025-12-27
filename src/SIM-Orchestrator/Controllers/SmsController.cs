using Microsoft.AspNetCore.Mvc;
using SIMOrchestrator.Models;
using SIMOrchestrator.Services;

namespace SIMOrchestrator.Controllers;

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
    public async Task<IActionResult> ReceiveSms([FromBody] SmsRequest request)
    {
        _logger.LogInformation("Received SMS from {Sender}", request.Sender);

        var sms = await _storage.SaveSmsAsync(request);

        var telegramSuccess = await _telegram.SendSmsToTelegramAsync(sms);

        if (telegramSuccess)
        {
            await _storage.MarkAsSentAsync(sms.Id);
        }
        else
        {
            _logger.LogWarning("Telegram send failed for SMS {Id}, will retry later", sms.Id);
        }

        return Ok(new { status = "received", id = sms.Id });
    }
}
