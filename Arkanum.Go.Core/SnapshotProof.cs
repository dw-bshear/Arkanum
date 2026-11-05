using System.Security.Cryptography;
using System.Text;

namespace Arkanum.Go.Core;

public sealed record Grant(string Schema, int SchemaVersion, string GrantId, string DeviceId, string VaultId,
    string UserSid, string DomainSid, long ContentVersion, string ContentHash,
    DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, long Revision, long DenyWatermark, bool Denied);
public sealed record SignedGrant(Grant Claims, byte[] Signature);
public sealed record EncryptedSnapshot(byte[] Nonce, byte[] Ciphertext, byte[] Tag);
// All context fields are trusted broker inputs, not strings accepted from the pipe client.
public sealed record OpenContext(string UserSid, string DeviceId, string VaultId, long ContentVersion,
    long MinimumRevision, long MinimumDenyWatermark);
public sealed record OpenResult(byte[]? Plaintext, LeaseState NextState);

public static class GrantCanonical
{
    // Fixed-order, length-prefixed strict UTF-8, little-endian numbers; UTC .NET ticks.
    // This is a proof wire format, not a general JSON canonicalizer.
    public static byte[] Encode(Grant grant)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true);
        writer.Write(grant.Schema); writer.Write(grant.SchemaVersion); writer.Write(grant.GrantId);
        writer.Write(grant.DeviceId); writer.Write(grant.VaultId); writer.Write(grant.UserSid); writer.Write(grant.DomainSid);
        writer.Write(grant.ContentVersion); writer.Write(grant.ContentHash);
        writer.Write(grant.IssuedAt.UtcTicks); writer.Write(grant.ExpiresAt.UtcTicks);
        writer.Write(grant.Revision); writer.Write(grant.DenyWatermark); writer.Write(grant.Denied);
        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>Isolated cryptographic proof; not a Windows broker, cache store, or server API.</summary>
public sealed class SnapshotProofReader : IDisposable
{
    private readonly RSA signer;

    // Pin comes from trusted installation/configuration, never from the grant/cache envelope.
    public SnapshotProofReader(byte[] pinnedSignerSubjectPublicKeyInfo)
    {
        signer = RSA.Create();
        try
        {
            signer.ImportSubjectPublicKeyInfo(pinnedSignerSubjectPublicKeyInfo, out var read);
            if (read != pinnedSignerSubjectPublicKeyInfo.Length || signer.KeySize < 2048)
                throw new CryptographicException("Invalid signer pin.");
        }
        catch { signer.Dispose(); throw; }
    }

    // No signature-valid boolean exists. Only Verify can produce this private capability.
    private sealed record VerifiedGrant(Grant Claims, byte[] CanonicalBytes);

    public OpenResult Open(SignedGrant? envelope, EncryptedSnapshot? snapshot, ReadOnlySpan<byte> key,
        OpenContext context, LeaseState state, ClockReading reading)
    {
        var clock = LeaseClock.Advance(state, reading);
        if (!clock.Allowed) return new(null, clock.NextState);
        try
        {
            var grant = Verify(envelope, context, clock.EffectiveUtc);
            return new(grant is null ? null : Decrypt(grant, snapshot, key), clock.NextState);
        }
        catch (CryptographicException) { return new(null, clock.NextState); }
        catch (FormatException) { return new(null, clock.NextState); }
        catch (ArgumentException) { return new(null, clock.NextState); }
    }

    private VerifiedGrant? Verify(SignedGrant? envelope, OpenContext context, DateTimeOffset now)
    {
        if (envelope?.Claims is not { } grant || envelope.Signature is null ||
            context.MinimumRevision < 0 || context.MinimumDenyWatermark < 0) return null;
        var bytes = GrantCanonical.Encode(grant);
        if (!signer.VerifyData(bytes, envelope.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) return null;
        if (grant.Schema != "arkanum-go-offline-grant" || grant.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(grant.GrantId) || string.IsNullOrWhiteSpace(grant.DeviceId) || string.IsNullOrWhiteSpace(grant.VaultId) ||
            grant.ContentVersion < 0 || grant.Revision < 0 || grant.DenyWatermark < 0 || grant.Denied ||
            grant.IssuedAt > now || grant.ExpiresAt <= now || grant.ExpiresAt <= grant.IssuedAt ||
            grant.ExpiresAt - grant.IssuedAt > TimeSpan.FromHours(48)) return null;
        AdSid.ParseUser(grant.UserSid, grant.DomainSid);
        AdSid.ParseUser(context.UserSid, grant.DomainSid);
        if (grant.UserSid != context.UserSid || grant.DeviceId != context.DeviceId || grant.VaultId != context.VaultId ||
            grant.ContentVersion != context.ContentVersion || grant.Revision < context.MinimumRevision || grant.DenyWatermark < context.MinimumDenyWatermark)
            return null;
        if (grant.ContentHash is null || grant.ContentHash.Length != 64 ||
            grant.ContentHash.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F'))) return null;
        return new(grant, bytes);
    }

    private static byte[]? Decrypt(VerifiedGrant grant, EncryptedSnapshot? snapshot, ReadOnlySpan<byte> key)
    {
        if (key.Length != 32 || snapshot?.Nonce is not { Length: 12 } || snapshot.Tag is not { Length: 16 } || snapshot.Ciphertext is null) return null;
        var plaintext = new byte[snapshot.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(snapshot.Nonce, snapshot.Ciphertext, snapshot.Tag, plaintext, grant.CanonicalBytes);
            if (CryptographicOperations.FixedTimeEquals(SHA256.HashData(plaintext), Convert.FromHexString(grant.Claims.ContentHash)))
                return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        CryptographicOperations.ZeroMemory(plaintext);
        return null;
    }

    public void Dispose() => signer.Dispose();
}
