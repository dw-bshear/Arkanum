using System.Security.Cryptography;
using Arkanum.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed record RecoveryKeyValidationResult(bool IsValid, string Message);

public sealed class SiteRecoveryKeyService(IMasterKeyProvider masterKeyProvider)
{
    public const string Prefix = "ark-recovery-v1.";
    public const string SingletonStateId = "site";
    private const string SentinelPlaintext = "arkanum-site-wide-recovery-sentinel:v1";

    public static string GenerateRecoveryKey()
    {
        var random = RandomNumberGenerator.GetBytes(32);
        return Prefix + Base64UrlEncode(random);
    }

    public static bool TryValidateRecoveryKeyFormat(string recoveryKey, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(recoveryKey))
        {
            error = "Recovery key is required.";
            return false;
        }

        if (!recoveryKey.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = $"Recovery key must start with {Prefix}.";
            return false;
        }

        var encoded = recoveryKey[Prefix.Length..];
        if (encoded.Length < 32)
        {
            error = "Recovery key does not contain enough key material.";
            return false;
        }

        return true;
    }

    public async Task<RecoveryStateEntity> EnsureRecoverySentinelAsync(ArkanumDbContext db, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var existing = await db.RecoveryStates.SingleOrDefaultAsync(s => s.Id == SingletonStateId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var encryption = new SecretEncryptionService(masterKeyProvider);
        var payload = encryption.Encrypt(SentinelPlaintext);
        var state = new RecoveryStateEntity
        {
            Id = SingletonStateId,
            RecoveryKeyId = payload.KeyId,
            EncryptedSentinel = payload.Ciphertext,
            EncryptionAlgorithm = payload.Algorithm,
            CreatedAt = now
        };
        db.RecoveryStates.Add(state);
        await db.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<RecoveryKeyValidationResult> ValidateRecoverySentinelAsync(ArkanumDbContext db, CancellationToken cancellationToken = default)
    {
        var state = await db.RecoveryStates.AsNoTracking().SingleOrDefaultAsync(s => s.Id == SingletonStateId, cancellationToken);
        if (state is null)
        {
            return new RecoveryKeyValidationResult(true, "No site recovery sentinel exists yet; it will be created during first-run setup or startup initialization.");
        }

        try
        {
            var encryption = new SecretEncryptionService(masterKeyProvider);
            var plaintext = encryption.Decrypt(new EncryptedSecretPayload(state.EncryptedSentinel, state.RecoveryKeyId, state.EncryptionAlgorithm));
            if (!string.Equals(plaintext, SentinelPlaintext, StringComparison.Ordinal))
            {
                return new RecoveryKeyValidationResult(false, "The recovery key does not match this database.");
            }

            return new RecoveryKeyValidationResult(true, "The site recovery key matches this database.");
        }
        catch (Exception)
        {
            return new RecoveryKeyValidationResult(false, "The recovery key does not match this database.");
        }
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
