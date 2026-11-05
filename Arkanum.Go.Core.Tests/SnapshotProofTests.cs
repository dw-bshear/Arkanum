using System.Security.Cryptography;
using System.Text;
using Arkanum.Go.Core;

namespace Arkanum.Go.Core.Tests;

public sealed class SnapshotProofTests : IDisposable
{
    private readonly RSA signer = RSA.Create(2048);
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] plaintext = Encoding.UTF8.GetBytes("{\"title\":\"Payroll master secret\",\"folder\":\"Executive private folder\",\"secret\":\"opaque-value\"}");
    private static DateTimeOffset Now => LeaseClockTests.Start;
    private static LeaseState State => new("boot-A", Now, TimeSpan.Zero, Now, TimeSpan.Zero);
    private static ClockReading Reading => new("boot-A", Now, TimeSpan.Zero);
    private static OpenContext Context => new(AdSidTests.User, "device-A", "vault-A", 7, 12, 4);
    private Grant Claims => new("arkanum-go-offline-grant", 1, "grant-A", "device-A", "vault-A", AdSidTests.User,
        AdSidTests.Domain, 7, Convert.ToHexString(SHA256.HashData(plaintext)), Now, Now.AddHours(48), 12, 4, false);

    private SignedGrant Sign(Grant claims, RSA? signingKey = null) => new(claims,
        (signingKey ?? signer).SignData(GrantCanonical.Encode(claims), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));

    private EncryptedSnapshot Encrypt(Grant grant, byte[]? content = null)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = content ?? plaintext;
        var cipher = new byte[data.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, data, cipher, tag, GrantCanonical.Encode(grant));
        return new(nonce, cipher, tag);
    }

    private OpenResult Open(SignedGrant? signed = null, EncryptedSnapshot? snapshot = null, OpenContext? context = null,
        ClockReading? reading = null, LeaseState? state = null, byte[]? encryptionKey = null)
    {
        using var reader = new SnapshotProofReader(signer.ExportSubjectPublicKeyInfo());
        return reader.Open(signed ?? Sign(Claims), snapshot ?? Encrypt(Claims), encryptionKey ?? key,
            context ?? Context, state ?? State, reading ?? Reading);
    }

    [Fact]
    public void Authorized_caller_decrypts_real_authenticated_snapshot() => Assert.Equal(plaintext, Open().Plaintext);

    [Theory]
    [InlineData("sid")] [InlineData("device")] [InlineData("vault")] [InlineData("version")]
    [InlineData("revision")] [InlineData("watermark")]
    public void Wrong_trusted_context_denies(string field)
    {
        var ctx = field switch
        {
            "sid" => Context with { UserSid = AdSidTests.Domain + "-1002" },
            "device" => Context with { DeviceId = "device-B" },
            "vault" => Context with { VaultId = "vault-B" },
            "version" => Context with { ContentVersion = 8 },
            "revision" => Context with { MinimumRevision = 13 },
            _ => Context with { MinimumDenyWatermark = 5 }
        };
        Assert.Null(Open(context: ctx).Plaintext);
    }

    [Theory]
    [InlineData("schema")] [InlineData("schemaVersion")] [InlineData("grantId")]
    [InlineData("device")] [InlineData("vault")] [InlineData("sid")] [InlineData("domain")]
    [InlineData("version")] [InlineData("hash")] [InlineData("issued")]
    [InlineData("expires")] [InlineData("revision")] [InlineData("watermark")] [InlineData("deny")]
    public void Every_signed_field_is_cryptographically_bound(string field)
    {
        var original = Claims;
        var modified = field switch
        {
            "schema" => original with { Schema = "other" }, "schemaVersion" => original with { SchemaVersion = 2 },
            "grantId" => original with { GrantId = "grant-B" }, "device" => original with { DeviceId = "device-B" },
            "vault" => original with { VaultId = "vault-B" }, "sid" => original with { UserSid = AdSidTests.Domain + "-1002" },
            "domain" => original with { DomainSid = "S-1-5-21-1-2-3" }, "version" => original with { ContentVersion = 8 },
            "hash" => original with { ContentHash = new string('0', 64) }, "issued" => original with { IssuedAt = Now.AddSeconds(-1) },
            "expires" => original with { ExpiresAt = Now.AddHours(47) }, "revision" => original with { Revision = 13 },
            "watermark" => original with { DenyWatermark = 5 }, _ => original with { Denied = true }
        };
        Assert.False(GrantCanonical.Encode(original).SequenceEqual(GrantCanonical.Encode(modified)));
        var signed = Sign(original) with { Claims = modified };
        Assert.Null(Open(signed, Encrypt(modified)).Plaintext);
    }

    [Theory]
    [InlineData("expired")] [InlineData("equalExpiry")] [InlineData("over48h")]
    [InlineData("future")] [InlineData("reversed")] [InlineData("denied")]
    [InlineData("schema")] [InlineData("schemaVersion")] [InlineData("domain")]
    [InlineData("emptyId")] [InlineData("negativeVersion")] [InlineData("negativeRevision")]
    [InlineData("negativeWatermark")] [InlineData("hash")]
    public void Even_authentically_signed_invalid_claims_deny(string reason)
    {
        var claims = reason switch
        {
            "expired" => Claims with { IssuedAt = Now.AddHours(-2), ExpiresAt = Now.AddHours(-1) },
            "equalExpiry" => Claims with { IssuedAt = Now.AddHours(-1), ExpiresAt = Now },
            "over48h" => Claims with { ExpiresAt = Now.AddHours(48).AddTicks(1) },
            "future" => Claims with { IssuedAt = Now.AddTicks(1) },
            "reversed" => Claims with { ExpiresAt = Now.AddTicks(-1) },
            "denied" => Claims with { Denied = true }, "schema" => Claims with { Schema = "other" },
            "schemaVersion" => Claims with { SchemaVersion = 2 },
            "domain" => Claims with { DomainSid = "S-1-5-21-1-2-3" },
            "emptyId" => Claims with { GrantId = "" }, "negativeVersion" => Claims with { ContentVersion = -1 },
            "negativeRevision" => Claims with { Revision = -1 }, "negativeWatermark" => Claims with { DenyWatermark = -1 },
            _ => Claims with { ContentHash = "not-a-sha256" }
        };
        Assert.Null(Open(Sign(claims), Encrypt(claims)).Plaintext);
    }

    [Theory]
    [InlineData("unsigned")] [InlineData("tampered")] [InlineData("wrongSigner")] [InlineData("pkcs1")]
    public void Unsigned_invalid_or_unpinned_signatures_deny(string reason)
    {
        using var other = RSA.Create(2048);
        var signed = reason switch
        {
            "unsigned" => new SignedGrant(Claims, []), "wrongSigner" => Sign(Claims, other),
            "pkcs1" => new SignedGrant(Claims, signer.SignData(GrantCanonical.Encode(Claims), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
            _ => Sign(Claims)
        };
        if (reason == "tampered") signed.Signature[0] ^= 1;
        Assert.Null(Open(signed).Plaintext);
    }

    [Fact]
    public void Null_unsigned_grant_denies()
    {
        using var reader = new SnapshotProofReader(signer.ExportSubjectPublicKeyInfo());
        Assert.Null(reader.Open(null, Encrypt(Claims), key, Context, State, Reading).Plaintext);
    }

    [Theory]
    [InlineData("cipher")] [InlineData("tag")] [InlineData("nonce")]
    public void Snapshot_tampering_denies(string field)
    {
        var snapshot = Encrypt(Claims);
        (field == "cipher" ? snapshot.Ciphertext : field == "tag" ? snapshot.Tag : snapshot.Nonce)[0] ^= 1;
        Assert.Null(Open(snapshot: snapshot).Plaintext);
    }

    [Fact]
    public void Different_valid_grant_cannot_reuse_snapshot_AAD()
    {
        var changed = Claims with { GrantId = "grant-B" };
        Assert.Null(Open(Sign(changed), Encrypt(Claims)).Plaintext);
    }

    [Fact]
    public void Plaintext_hash_is_checked_even_after_valid_GCM_authentication() =>
        Assert.Null(Open(snapshot: Encrypt(Claims, Encoding.UTF8.GetBytes("different content"))).Plaintext);

    [Fact]
    public void Metadata_and_secrets_are_inside_ciphertext_not_envelope()
    {
        var snapshot = Encrypt(Claims);
        var bytes = snapshot.Nonce.Concat(snapshot.Ciphertext).Concat(snapshot.Tag).ToArray();
        Assert.DoesNotContain("Payroll master secret", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("Executive private folder", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("opaque-value", Encoding.UTF8.GetString(bytes));
        Assert.Equal(plaintext, Open(snapshot: snapshot).Plaintext);
    }

    [Fact]
    public void Failure_advances_state_and_never_resets_lease()
    {
        var atHour = Reading with { Utc = Now.AddHours(1), Elapsed = TimeSpan.FromHours(1) };
        var failure = Open(new(Claims, []), reading: atHour);
        Assert.Null(failure.Plaintext);
        Assert.Equal(Now.AddHours(1), failure.NextState.HighWaterUtc);
        Assert.Equal(State.AnchorUtc, failure.NextState.AnchorUtc);
        Assert.Null(Open(state: failure.NextState, reading: Reading).Plaintext);
    }

    [Fact]
    public void Monotonic_expiry_denies_even_if_wall_time_is_frozen() =>
        Assert.Null(Open(reading: Reading with { Elapsed = TimeSpan.FromHours(48) }).Plaintext);

    [Fact]
    public void Boot_change_preserves_access_to_valid_signed_snapshot() =>
        Assert.Equal(plaintext, Open(reading: Reading with { BootId = "boot-B" }).Plaintext);

    [Theory]
    [InlineData(48)]
    [InlineData(60)]
    public void Powered_off_across_signed_expiry_denies_snapshot(int hours)
    {
        var signed = Sign(Claims);
        var snapshot = Encrypt(Claims);
        var before = Open(signed, snapshot, reading: Reading with { Utc = Now.AddHours(1), Elapsed = TimeSpan.FromHours(1) });
        Assert.Equal(plaintext, before.Plaintext);
        var after = Open(signed, snapshot, state: before.NextState,
            reading: new("boot-B", Now.AddHours(hours), TimeSpan.Zero));
        Assert.Null(after.Plaintext);
        Assert.Equal(Claims.ExpiresAt, signed.Claims.ExpiresAt);
        Assert.Equal(Now.AddHours(hours), after.NextState.EffectiveHighWaterUtc);
    }

    [Fact]
    public void Same_signed_snapshot_survives_repeated_boots_only_until_original_expiry()
    {
        var signed = Sign(Claims);
        var snapshot = Encrypt(Claims);
        var state = State;
        foreach (var hour in new[] { 1, 24, 47, 48, 49 })
        {
            var result = Open(signed, snapshot, state: state, reading: new($"boot-{hour}", Now.AddHours(hour), TimeSpan.Zero));
            if (hour < 48) Assert.Equal(plaintext, result.Plaintext);
            else Assert.Null(result.Plaintext);
            Assert.Equal(Now.AddHours(48), signed.Claims.ExpiresAt);
            state = result.NextState;
        }
    }

    [Fact]
    public void Repeated_frozen_wall_reads_work_until_monotonic_expiry()
    {
        var state = State;
        foreach (var hours in new[] { 1, 2, 47 })
        {
            var result = Open(state: state, reading: Reading with { Elapsed = TimeSpan.FromHours(hours) });
            Assert.Equal(plaintext, result.Plaintext);
            state = result.NextState;
        }
        Assert.Null(Open(state: state,
            reading: Reading with { Elapsed = TimeSpan.FromHours(48) }).Plaintext);
    }

    [Theory]
    [InlineData(0)] [InlineData(16)] [InlineData(24)] [InlineData(33)]
    public void Only_AES256_key_is_accepted(int length) => Assert.Null(Open(encryptionKey: new byte[length]).Plaintext);

    [Fact]
    public void Canonical_encoding_is_stable_and_utc_normalized()
    {
        Assert.Equal(GrantCanonical.Encode(Claims), GrantCanonical.Encode(Claims with { IssuedAt = Now.ToOffset(TimeSpan.FromHours(2)), ExpiresAt = Claims.ExpiresAt.ToOffset(TimeSpan.FromHours(-3)) }));
    }

    [Theory]
    [InlineData("revision")] [InlineData("watermark")]
    public void Invalid_context_floors_deny(string field)
    {
        var context = field == "revision" ? Context with { MinimumRevision = -1 } : Context with { MinimumDenyWatermark = -1 };
        Assert.Null(Open(context: context).Plaintext);
    }

    [Theory]
    [InlineData("null")] [InlineData("nonce")] [InlineData("tag")] [InlineData("cipher")]
    public void Missing_snapshot_components_deny_and_still_advance_state(string field)
    {
        var snapshot = field switch
        {
            "null" => null,
            "nonce" => Encrypt(Claims) with { Nonce = null! },
            "tag" => Encrypt(Claims) with { Tag = null! },
            _ => Encrypt(Claims) with { Ciphertext = null! }
        };
        using var reader = new SnapshotProofReader(signer.ExportSubjectPublicKeyInfo());
        var result = reader.Open(Sign(Claims), snapshot!, key, Context, State,
            Reading with { Utc = Now.AddHours(1), Elapsed = TimeSpan.FromHours(1) });
        Assert.Null(result.Plaintext);
        Assert.Equal(Now.AddHours(1), result.NextState.HighWaterUtc);
    }

    public void Dispose() { signer.Dispose(); CryptographicOperations.ZeroMemory(key); }
}
