using System.Security.Cryptography;
using System.Text;
using SIMOrchestrator.Models;

namespace SIMOrchestrator.Services;

/// <summary>
/// Builds the key that makes device retries idempotent. A device-assigned
/// <see cref="SmsRequest.MessageId"/> wins; otherwise the key is a hash of the
/// content, so re-sending the same SMS after a lost 200 OK creates no duplicate.
/// </summary>
public static class DedupKey
{
    public static string Compute(SmsRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.MessageId))
            return "id:" + request.MessageId.Trim();

        var material = string.Join('\n', request.Sender, request.Timestamp ?? string.Empty, request.Text);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
