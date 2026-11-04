using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SIMOrchestrator.HealthChecks;

/// <summary>
/// Writes the public health payload. It deliberately contains only statuses:
/// exception messages and descriptions never leave the process because the
/// endpoint is reachable without authentication.
/// </summary>
public static class HealthResponseWriter
{
    public const string DatabaseCheckName = "database";

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var database = report.Entries.TryGetValue(DatabaseCheckName, out var db)
                       && db.Status == HealthStatus.Healthy
            ? "connected"
            : "unavailable";

        int? unsentMessages = null;
        int? failedMessages = null;
        if (report.Entries.TryGetValue(SmsBacklogHealthCheck.Name, out var backlog))
        {
            unsentMessages = ReadCount(backlog, SmsBacklogHealthCheck.UnsentMessagesKey);
            failedMessages = ReadCount(backlog, SmsBacklogHealthCheck.FailedMessagesKey);
        }

        var payload = new
        {
            status = report.Status switch
            {
                HealthStatus.Healthy => "healthy",
                HealthStatus.Degraded => "degraded",
                _ => "unhealthy"
            },
            timestamp = DateTimeOffset.UtcNow,
            database,
            unsentMessages,
            failedMessages,
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => e.Value.Status.ToString().ToLowerInvariant())
        };

        return context.Response.WriteAsJsonAsync(payload);
    }

    private static int? ReadCount(HealthReportEntry entry, string key)
        => entry.Data.TryGetValue(key, out var value) && value is int count ? count : null;
}
