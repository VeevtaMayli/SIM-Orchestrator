using Microsoft.AspNetCore.Mvc;
using SIMOrchestrator.Logging;
using SIMOrchestrator.Models;
using SIMOrchestrator.Services;

namespace SIMOrchestrator.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SmsController : ControllerBase
{
    private readonly ISmsStorageService _storage;
    private readonly IDeliveryQueue _queue;
    private readonly ILogger<SmsController> _logger;

    public SmsController(
        ISmsStorageService storage,
        IDeliveryQueue queue,
        ILogger<SmsController> logger)
    {
        _storage = storage;
        _queue = queue;
        _logger = logger;
    }

    /// <summary>
    /// Stores the SMS and answers immediately; delivery to Telegram happens in the
    /// background. Re-sending the same SMS (device retry after a lost response)
    /// returns the original id and does not create a second copy.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReceiveSms([FromBody] SmsRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received SMS from {Sender}", SenderMasker.Mask(request.Sender));

        var result = await _storage.SaveSmsAsync(request, cancellationToken);

        if (result.IsDuplicate)
            return Ok(new { status = "duplicate", id = result.Sms.Id });

        _queue.Signal();
        return Ok(new { status = "received", id = result.Sms.Id });
    }
}
