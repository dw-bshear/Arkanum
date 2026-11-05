using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Arkanum.Go.Core;
using Arkanum.Go.Proof.Protocol;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;

namespace Arkanum.Go.Proof.Server;

public sealed class ProofService(ProofConfiguration configuration, RSA signer)
{
    // Made-up test data only: never read from the portal, directory, or a real vault.
    public const string SyntheticPayload = """
        {"testData":true,"label":"SYNTHETIC ARKANUM GO PROOF ONLY - NO PRODUCTION SECRETS","credentials":[{"id":"synthetic-credential","title":"Synthetic test credential","username":"made-up-user@test.invalid","password":"MADE-UP-TEST-PASSWORD-ONLY"}],"secureNotes":[{"id":"synthetic-secure-note","title":"Synthetic test secure note","text":"MADE-UP-TEST-SECURE-NOTE-ONLY"}]}
        """;
    private readonly object gate = new();
    private sealed record DeviceState(long Sequence, bool Denied, DateTimeOffset IssuedAt, string[]? AuthorizedBindings = null);

    public ProofSyncResponse Sync(ProofSyncRequest request, X509Certificate2 certificate)
    {
        configuration.ValidateLease();
        configuration.ValidateEnrollmentDirectory();
        if (request is null || string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 128 ||
            request.Nonce is null || request.Nonce.Length != 44) throw new ArgumentException("Invalid proof request.");
        try { if (Convert.FromBase64String(request.Nonce).Length != 32) throw new ArgumentException("Invalid nonce."); }
        catch (FormatException) { throw new ArgumentException("Invalid nonce."); }
        var matches = configuration.Devices.Where(d => d.Id == request.DeviceId).ToArray();
        if (matches.Length != 1 || configuration.Devices.Count(d => d.CertificateSha256 == matches[0].CertificateSha256) != 1 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(matches[0].CertificateSha256),
                Encoding.ASCII.GetBytes(certificate.GetCertHashString(HashAlgorithmName.SHA256))))
            throw new UnauthorizedAccessException("Unregistered device certificate.");
        var device = matches[0];
        using var authenticationKey = certificate.GetRSAPublicKey();
        if (authenticationKey is null || authenticationKey.KeySize < 2048 ||
            DateTimeOffset.UtcNow < certificate.NotBefore.ToUniversalTime() || DateTimeOffset.UtcNow > certificate.NotAfter.ToUniversalTime())
            throw new UnauthorizedAccessException("Invalid device certificate.");
        using var unwrap = RSA.Create();
        var spki = Convert.FromBase64String(device.UnwrapSpki);
        unwrap.ImportSubjectPublicKeyInfo(spki, out var read);
        if (configuration.Devices.Count(d => d.UnwrapSpki == device.UnwrapSpki) != 1 ||
            read != spki.Length || unwrap.KeySize < 2048 || device.UnwrapSpki != Convert.ToBase64String(unwrap.ExportSubjectPublicKeyInfo()) ||
            CryptographicOperations.FixedTimeEquals(unwrap.ExportSubjectPublicKeyInfo(), authenticationKey.ExportSubjectPublicKeyInfo()))
            throw new UnauthorizedAccessException("Separate unwrap key required.");
        lock (gate)
        {
            Directory.CreateDirectory(configuration.StateDirectory);
            var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(device.Id)));
            // Cross-process exclusion; fail closed rather than racing a sequence write.
            using var stateLock = new FileStream(Path.Combine(configuration.StateDirectory, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var path = Path.Combine(configuration.StateDirectory, name + ".json");
            Directory.CreateDirectory(configuration.EnrollmentDirectory);
            var markerPath = Path.Combine(configuration.EnrollmentDirectory, name + ".enrolled");
            var established = File.Exists(markerPath);
            var hasState = File.Exists(path);
            if (established && !hasState)
                throw new InvalidDataException("Established enrollment state is missing; explicit re-enrollment required.");
            // A marker is written and flushed BEFORE first issuance. Losing sequence state
            // can never silently bootstrap that established enrollment again. Keep this
            // directory independently protected from ordinary sequence-state maintenance.
            if (!established)
            {
                using var marker = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                JsonSerializer.Serialize(marker, new { device.Id, device.IssuedAt }); marker.Flush(true);
            }
            var state = hasState
                ? JsonSerializer.Deserialize<DeviceState>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid state.")
                : new DeviceState(0, false, device.IssuedAt, []);
            var now = DateTimeOffset.UtcNow;
            // Enrollment issuance is immutable provenance, not the renewable grant clock.
            var expires = now.AddSeconds(configuration.LeaseSeconds);
            var certificateExpiry = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
            if (certificateExpiry < expires) expires = certificateExpiry;
            var eligible = new List<(ProofUser User, string Vault)>();
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            var allowed = !state.Denied && device.Approved && state.IssuedAt == device.IssuedAt &&
                state.IssuedAt != default && state.IssuedAt <= now && expires > now &&
                state.AuthorizedBindings is not null && (!hasState || established) && device.VaultIds.Length > 0;
            foreach (var vaultId in device.VaultIds.Distinct(StringComparer.Ordinal))
            {
                var vaults = configuration.Vaults.Where(v => v.Id == vaultId).ToArray();
                if (vaults.Length != 1 || !vaults[0].OfflineEnabled || vaults[0].MemberUserIds.Length == 0)
                { allowed = false; continue; }
                // Membership in each device-assigned vault, plus effective Web reveal policy,
                // is the sole user authorization source. Legacy device.UserIds is ignored.
                foreach (var userId in vaults[0].MemberUserIds.Distinct(StringComparer.Ordinal))
                {
                    var users = configuration.Users.Where(u => u.Id == userId).ToArray();
                    if (users.Length != 1 || configuration.Users.Count(u => u.Sid == users[0].Sid) != 1 ||
                        configuration.Users.Count(u => u.ObjectGuid == users[0].ObjectGuid) != 1 || !Eligible(users[0], now))
                    { allowed = false; continue; }
                    var user = users[0];
                    eligible.Add((user, vaultId));
                    bindings.Add(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                        new[] { vaultId, user.Sid, user.ObjectGuid.ToString("D") }))));
                }
            }
            // The existing broker protocol merges packages; it has no signed per-SID deletion.
            // Never allow a partial refresh to strand a previously authorized cached grant.
            // Old state without a complete history also requires conservative signed deny.
            if (state.AuthorizedBindings is null || state.AuthorizedBindings.Any(b => !bindings.Contains(b))) allowed = false;
            var denied = !allowed || eligible.Count == 0;
            var packages = denied ? [] : eligible.Select(e => Package(device, e.User, e.Vault, now, expires, unwrap)).ToArray();
            var next = state with { Sequence = checked(state.Sequence + 1), Denied = denied,
                AuthorizedBindings = denied ? state.AuthorizedBindings : bindings.Order(StringComparer.Ordinal).ToArray() };
            var response = new ProofSyncResponse(ProofWire.ProtocolId, device.Id, request.Nonce, next.Sequence, now,
                packages, denied, []);
            response = response with { Signature = Sign(ProofWire.EncodeUnsigned(response)) };
            var temporary = path + ".new";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, next); file.Flush(true); }
            File.Move(temporary, path, true); // durable state before the response can leave the process
            return response;
        }
    }

    private static bool Eligible(ProofUser user, DateTimeOffset now)
    {
        if (!user.Enabled || user.ObjectGuid == Guid.Empty || string.IsNullOrWhiteSpace(user.Domain) ||
            string.IsNullOrWhiteSpace(user.VerifiedBy) || user.VerifiedAt == default || user.VerifiedAt > now) return false;
        try { AdSid.ParseUser(user.Sid, user.DomainSid); }
        catch (Exception e) when (e is FormatException or ArgumentException) { return false; }
        var policy = new VaultAuthorizationService();
        return policy.CanRevealAnySecret(new ArkanumUser { Roles = user.Roles.ToList() });
    }

    private ProofPackage Package(ProofDevice device, ProofUser user, string vault, DateTimeOffset issued, DateTimeOffset expires, RSA unwrap)
    {
        var bytes = Encoding.UTF8.GetBytes(SyntheticPayload);
        var claims = new Grant("arkanum-go-offline-grant", 1,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{device.Id}|{user.ObjectGuid:D}|{vault}|{issued.UtcTicks}"))),
            device.Id, vault, user.Sid, user.DomainSid, 1, Convert.ToHexString(SHA256.HashData(bytes)),
            issued, expires, 1, 0, false);
        var aad = GrantCanonical.Encode(claims);
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12); var cipher = new byte[bytes.Length]; var tag = new byte[16];
            using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, bytes, cipher, tag, aad);
            return new(new(claims, Sign(aad)), new(nonce, cipher, tag), unwrap.Encrypt(key, RSAEncryptionPadding.OaepSHA256));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private byte[] Sign(byte[] bytes)
    {
        lock (signer) return signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }
}
