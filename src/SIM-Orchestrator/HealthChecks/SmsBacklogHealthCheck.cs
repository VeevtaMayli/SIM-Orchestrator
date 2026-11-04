using Microsoft.Extensions.Diagnostics.HealthChecks;
using SIMOrchestrator.Services;

namespace SIMOrchestrator.HealthChecks;

/// <summary>
/// Reports how many SMS are still waiting for delivery and how many were given up on.
/// Permanently failed messages make the service Degraded: it still works, but someone
/// has to look at them.
/// </summary>
public sealed class SmsBacklogHealthCheck : IHealthCheck
{
    public const string Name = "backlog";
    public const string UnsentMessagesKey = "unsentMessages";
    public const string FailedMessagesKey = "failedMessages";

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
        var failed = await _storage.CountFailedAsync(cancellationToken);

        var data = new Dictionary<string, object>
        {
            [UnsentMessagesKey] = unsent,
            [FailedMessagesKey] = failed
        };

        return failed > 0
            ? HealthCheckResult.Degraded($"{failed} message(s) failed permanently", data: data)
            : HealthCheckResult.Healthy(data: data);
    }
}
