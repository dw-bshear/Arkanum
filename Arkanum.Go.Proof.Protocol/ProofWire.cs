using System.Security.Cryptography;
using System.Text;
using Arkanum.Go.Core;
namespace Arkanum.Go.Proof.Protocol;

public sealed record ProofSyncRequest(string DeviceId, string Nonce);
public sealed record ProofPackage(SignedGrant Grant, EncryptedSnapshot Snapshot, byte[] WrappedKey);
public sealed record ProofSyncResponse(string Protocol, string DeviceId, string Nonce, long Sequence,
    DateTimeOffset ServerUtc, ProofPackage[] Packages, bool Denied, byte[] Signature);

public static class ProofWire
{
    public const string ProtocolId = "arkanum-go-sprint1/1";
    // Strict UTF8 BinaryWriter strings (7-bit byte length), LE numbers, UTC .NET ticks;
    // byte arrays use signed Int32 length. Package order is significant. Signature excluded.
    public static byte[] EncodeUnsigned(ProofSyncResponse response)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        writer.Write("ARKANUM-GO-PROOF-SYNC-RESPONSE\0");
        writer.Write(response.Protocol); writer.Write(response.DeviceId); writer.Write(response.Nonce);
        writer.Write(response.Sequence); writer.Write(response.ServerUtc.UtcTicks); writer.Write(response.Denied);
        writer.Write(response.Packages.Length);
        foreach (var package in response.Packages)
        {
            Bytes(GrantCanonical.Encode(package.Grant.Claims)); Bytes(package.Grant.Signature);
            Bytes(package.Snapshot.Nonce); Bytes(package.Snapshot.Ciphertext); Bytes(package.Snapshot.Tag);
            Bytes(package.WrappedKey);
        }
        writer.Flush();
        return stream.ToArray();
        void Bytes(byte[] value) { writer.Write(value.Length); writer.Write(value); }
    }
}

// The caller must durably commit Sequence + deny latch before exposing packages.
// A true return means authenticated, not authorized: Denied still means erase cache/keys.
public sealed class ProofResponseVerifier
{
    private readonly byte[] signerSpki;
    public ProofResponseVerifier(byte[] signerSpki) => this.signerSpki = signerSpki.ToArray();
    public bool Accept(ProofSyncResponse response, ProofSyncRequest request, long lastSequence, bool denyLatched)
    {
        try
        {
            if (denyLatched || lastSequence < 0 || response.Protocol != ProofWire.ProtocolId ||
                response.DeviceId != request.DeviceId || response.Nonce != request.Nonce ||
                response.Sequence <= lastSequence || response.Sequence <= 0 ||
                response.Packages is null || response.Signature is null ||
                (response.Denied ? response.Packages.Length != 0 : response.Packages.Length == 0)) return false;
            using var rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(signerSpki, out var consumed);
            return consumed == signerSpki.Length && rsa.KeySize >= 2048 &&
                rsa.VerifyData(ProofWire.EncodeUnsigned(response), response.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (Exception error) when (error is CryptographicException or ArgumentException or NullReferenceException or IOException)
        { return false; }
    }
}
