using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIMOrchestrator.Data;

namespace SIMOrchestrator.Controllers;

[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<HealthController> _logger;

    public HealthController(AppDbContext context, ILogger<HealthController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        try
        {
            await _context.Database.CanConnectAsync();

            var unsentCount = await _context.SmsMessages.CountAsync(s => !s.SentToTelegram);

            return Ok(new
            {
                status = "healthy",
                timestamp = DateTimeOffset.UtcNow,
                database = "connected",
                unsentMessages = unsentCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(503, new
            {
                status = "unhealthy",
                timestamp = DateTimeOffset.UtcNow,
                error = ex.Message
            });
        }
    }
}
