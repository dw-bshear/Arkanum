using System.Security.Cryptography;
using System.Text;
using Arkanum.Web.Data;

namespace Arkanum.Web.Services;

public static class AuditEventIntegrity
{
    public const string Algorithm = "SHA-256";

    public static void Stamp(AuditEventEntity auditEvent, long sequence, string previousHash)
    {
        auditEvent.Sequence = sequence;
        auditEvent.PreviousIntegrityHash = previousHash;
        auditEvent.IntegrityAlgorithm = Algorithm;
        auditEvent.IntegrityHash = ComputeHash(auditEvent);
    }

    public static bool Verify(AuditEventEntity auditEvent)
    {
        return string.Equals(auditEvent.IntegrityHash, ComputeHash(auditEvent), StringComparison.OrdinalIgnoreCase);
    }

    public static string ComputeHash(AuditEventEntity auditEvent)
    {
        var canonical = string.Join("\n", new[]
        {
            auditEvent.Id,
            auditEvent.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            auditEvent.PreviousIntegrityHash,
            auditEvent.Type.ToString(),
            auditEvent.ActorUserKey,
            auditEvent.OccurredAt.ToUniversalTime().ToString("O"),
            auditEvent.Target,
            auditEvent.Message
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
