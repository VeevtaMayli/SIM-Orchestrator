using Microsoft.Extensions.Diagnostics.HealthChecks;
using SIMOrchestrator.Services;

namespace SIMOrchestrator.HealthChecks;

/// <summary>Reports how many SMS are still waiting to be delivered to Telegram.</summary>
public sealed class SmsBacklogHealthCheck : IHealthCheck
{
    public const string Name = "backlog";
    public const string UnsentMessagesKey = "unsentMessages";

    private readonly ISmsStorageService _storage;

    public SmsBacklogHealthCheck(ISmsStorageService storage)
    {
        _storage = storage;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var unsent = await _storage.CountUnsentAsync(cancellationToken);
        var data = new Dictionary<string, object> { [UnsentMessagesKey] = unsent };
        return HealthCheckResult.Healthy(data: data);
    }
}
