using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arkanum.Go.Core;
using Arkanum.Go.Proof.Protocol;
using Arkanum.Go.Proof.Server;
using Arkanum.Web.Domain;

namespace Arkanum.Go.Proof.Tests;

public sealed class OperationalTests
{
    private static ProofConfiguration Load(Fixture f, int? seconds)
    {
        Directory.CreateDirectory(f.Config.StateDirectory);
        var json = JsonSerializer.SerializeToNode(f.Config)!.AsObject();
        if (seconds.HasValue) json["LeaseSeconds"] = seconds.Value;
        else json.Remove("LeaseSeconds");
        var path = Path.Combine(f.Config.StateDirectory, "registry.json");
        File.WriteAllText(path, json.ToJsonString());
        return ProofServerProgram.LoadConfiguration(path);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("relative")]
    [InlineData("same")]
    [InlineData("nested")]
    public void Enrollment_marker_directory_requires_independent_absolute_location(string invalid)
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.Config.StateDirectory);
        var json = JsonSerializer.SerializeToNode(f.Config)!;
        json["EnrollmentDirectory"] = invalid switch
        {
            "missing" => "", "relative" => "relative-enrollment-markers",
            "same" => f.Config.StateDirectory, _ => Path.Combine(f.Config.StateDirectory, "markers")
        };
        var path = Path.Combine(f.Config.StateDirectory, "registry.json"); File.WriteAllText(path, json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => ProofServerProgram.LoadConfiguration(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Established_enrollment_never_recreates_missing_sequence_state(bool denied)
    {
        using var f = new Fixture(); var first = f.Service().Sync(f.Request(), f.Client);
        if (denied) { f.Config.Devices[0].Approved = false; Assert.True(f.Service().Sync(f.Request(), f.Client).Denied); }
        var state = Assert.Single(Directory.GetFiles(f.Config.StateDirectory, "*.json"));
        File.Delete(state); f.Config.Devices[0].Approved = true;
        Assert.Throws<InvalidDataException>(() => f.Service().Sync(f.Request(), f.Client));
        Assert.False(File.Exists(state));
        // Loss is isolated to this enrollment, not a global reset or outage.
        using var pristine = new Fixture();
        pristine.Config.StateDirectory = f.Config.StateDirectory;
        pristine.Config.EnrollmentDirectory = f.Config.EnrollmentDirectory;
        pristine.Config.Devices[0].Id = "unrelated-synthetic-enrollment";
        Assert.False(pristine.Service().Sync(new(pristine.Config.Devices[0].Id, pristine.Request().Nonce), pristine.Client).Denied);
        Assert.Throws<InvalidDataException>(() => f.Service().Sync(f.Request(), f.Client));
        Assert.False(File.Exists(state));
    }

    [Fact]
    public void Legacy_state_without_complete_authorization_history_is_conservatively_denied()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.Config.StateDirectory);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(f.Config.Devices[0].Id)));
        File.WriteAllText(Path.Combine(f.Config.StateDirectory, name + ".json"), JsonSerializer.Serialize(new { Sequence = 7, Denied = false, f.Config.Devices[0].IssuedAt }));
        var response = f.Service().Sync(f.Request(), f.Client);
        Assert.True(response.Denied); Assert.Empty(response.Packages); Assert.Equal(8, response.Sequence);
    }

    [Fact]
    public void Legacy_user_ids_are_optional_ignored_even_when_null()
    {
        using var f = new Fixture(); f.Config.Devices[0].UserIds = null!;
        var config = Load(f, null);
        Assert.False(new ProofService(config, f.Signer).Sync(f.Request(), f.Client).Denied);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(172801)]
    public void Service_itself_rejects_invalid_lease_even_without_file_loader(int seconds)
    {
        using var f = new Fixture();
        var node = JsonSerializer.SerializeToNode(f.Config)!; node["LeaseSeconds"] = seconds;
        var config = node.Deserialize<ProofConfiguration>()!;
        Assert.Throws<InvalidDataException>(() => new ProofService(config, f.Signer).Sync(f.Request(), f.Client));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(172801)]
    [InlineData(int.MaxValue)]
    public void Operator_lease_outside_one_second_to_48_hours_is_rejected(int seconds)
    {
        using var f = new Fixture();
        Assert.Throws<InvalidDataException>(() => Load(f, seconds));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(90)]
    [InlineData(172800)]
    [InlineData(null)]
    public void Operator_lease_sets_signed_window_with_48_hour_default(int? seconds)
    {
        using var f = new Fixture(); var config = Load(f, seconds);
        var response = new ProofService(config, f.Signer).Sync(f.Request(), f.Client);
        var claims = Assert.Single(response.Packages).Grant.Claims;
        Assert.Equal(response.ServerUtc, claims.IssuedAt);
        Assert.Equal(TimeSpan.FromSeconds(seconds ?? 172800), claims.ExpiresAt - claims.IssuedAt);
    }

    [Fact]
    public void Auth_certificate_expiry_caps_the_signed_grant()
    {
        using var f = new Fixture(); using var key = RSA.Create(2048);
        var req = new CertificateRequest("CN=synthetic-short-lived-test-only", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(2));
        f.Config.Devices[0].CertificateSha256 = cert.GetCertHashString(HashAlgorithmName.SHA256);
        var response = f.Service().Sync(f.Request(), cert);
        Assert.Equal(new DateTimeOffset(cert.NotAfter.ToUniversalTime()), Assert.Single(response.Packages).Grant.Claims.ExpiresAt);
    }

    [Fact]
    public void Successful_unchanged_authenticated_refresh_renews_old_enrollment_not_old_signed_grant()
    {
        using var f = new Fixture(); f.Config.Devices[0].IssuedAt = DateTimeOffset.UtcNow.AddDays(-10);
        var request = f.Request(); var first = f.Service().Sync(request, f.Client);
        Assert.False(first.Denied); var old = Assert.Single(first.Packages);
        // A fresh service instance uses durable enrollment binding, not a new enrollment.
        var nextRequest = f.Request(); var next = f.Service().Sync(nextRequest, f.Client);
        Assert.False(next.Denied); var renewed = Assert.Single(next.Packages);
        Assert.Equal(first.ServerUtc, old.Grant.Claims.IssuedAt);
        Assert.Equal(next.ServerUtc, renewed.Grant.Claims.IssuedAt);
        Assert.True(renewed.Grant.Claims.IssuedAt > old.Grant.Claims.IssuedAt);
        Assert.True(renewed.Grant.Claims.ExpiresAt > old.Grant.Claims.ExpiresAt);
        Assert.Equal(old.Grant.Claims.ContentHash, renewed.Grant.Claims.ContentHash);
        Assert.Equal(old.Grant.Claims.ContentVersion, renewed.Grant.Claims.ContentVersion);
        Assert.True(next.Sequence > first.Sequence);
        var verifier = new ProofResponseVerifier(f.Signer.ExportSubjectPublicKeyInfo());
        Assert.True(verifier.Accept(next, nextRequest, first.Sequence, false));
        Assert.False(verifier.Accept(first, nextRequest, first.Sequence, false));
        var edited = old with { Grant = old.Grant with { Claims = old.Grant.Claims with { ExpiresAt = renewed.Grant.Claims.ExpiresAt } } };
        Assert.False(f.Signer.VerifyData(GrantCanonical.Encode(edited.Grant.Claims), edited.Grant.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        Assert.False(verifier.Accept(first with { Packages = [edited] }, request, 0, false));
    }

    [Fact]
    public void Failed_requests_do_not_mutate_sequence_or_renew_and_signed_denial_remains_sticky()
    {
        using var f = new Fixture(); var first = f.Service().Sync(f.Request(), f.Client);
        var files = Directory.GetFiles(f.Config.StateDirectory, "*.json"); var state = File.ReadAllBytes(Assert.Single(files));
        using var other = Fixture.Cert("failed-refresh-test-only", false);
        Assert.Throws<UnauthorizedAccessException>(() => f.Service().Sync(f.Request(), other));
        Assert.Throws<ArgumentException>(() => f.Service().Sync(f.Request() with { Nonce = "invalid" }, f.Client));
        Assert.Equal(state, File.ReadAllBytes(files[0]));
        f.Config.Devices[0].Approved = false;
        var denied = f.Service().Sync(f.Request(), f.Client);
        Assert.True(denied.Denied); Assert.Empty(denied.Packages); Assert.Equal(first.Sequence + 1, denied.Sequence);
        f.Config.Devices[0].Approved = true;
        var latched = f.Service().Sync(f.Request(), f.Client); Assert.True(latched.Denied); Assert.Empty(latched.Packages);
    }

    private static ProofUser User(string id, int rid) => new()
    {
        Id = id, Sid = $"S-1-5-21-101-202-303-{rid}", DomainSid = "S-1-5-21-101-202-303",
        ObjectGuid = Guid.NewGuid(), Domain = "test.invalid", VerifiedBy = "synthetic-test-only",
        VerifiedAt = DateTimeOffset.UtcNow, Enabled = true, Roles = [ArkanumRole.User]
    };

    [Fact]
    public void Vault_membership_and_device_vault_assignment_are_the_only_assignment_authorities()
    {
        using var f = new Fixture(); var second = User("second-synthetic-user", 1002); var unassigned = User("unassigned-synthetic-user", 1003);
        f.Config.Users = [f.Config.Users[0], second, unassigned];
        f.Config.Devices[0].UserIds = [unassigned.Id]; // Legacy field must neither grant nor restrict.
        f.Config.Vaults[0].MemberUserIds = ["test-user", second.Id];
        var response = f.Service().Sync(f.Request(), f.Client);
        Assert.False(response.Denied);
        Assert.Equal(2, response.Packages.Length);
        Assert.Equal(new[] { f.Config.Users[0].Sid, second.Sid }.Order(), response.Packages.Select(p => p.Grant.Claims.UserSid).Order());
        f.Config.Devices[0].UserIds = [];
        Assert.False(f.Service().Sync(f.Request(), f.Client).Denied);
    }

    [Fact]
    public void Different_assigned_vaults_do_not_require_cross_vault_user_membership()
    {
        using var f = new Fixture(); var second = User("other-vault-synthetic-user", 1002);
        f.Config.Users = [f.Config.Users[0], second]; f.Config.Devices[0].UserIds = [];
        f.Config.Vaults = [f.Config.Vaults[0], new() { Id = "other-synthetic-vault", OfflineEnabled = true, MemberUserIds = [second.Id] }];
        f.Config.Devices[0].VaultIds = ["synthetic", "other-synthetic-vault"];
        var response = f.Service().Sync(f.Request(), f.Client);
        Assert.False(response.Denied); Assert.Equal(2, response.Packages.Length);
        Assert.Contains(response.Packages, p => p.Grant.Claims.VaultId == "synthetic" && p.Grant.Claims.UserSid == f.Config.Users[0].Sid);
        Assert.Contains(response.Packages, p => p.Grant.Claims.VaultId == "other-synthetic-vault" && p.Grant.Claims.UserSid == second.Sid);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("removed-user")]
    [InlineData("removed-membership")]
    [InlineData("removed-vault-assignment")]
    [InlineData("lost-reveal")]
    public void Previously_authorized_sid_cannot_survive_in_cache_via_partial_refresh(string change)
    {
        using var f = new Fixture(); var second = User("second-synthetic-user", 1002);
        f.Config.Users = [f.Config.Users[0], second];
        f.Config.Vaults[0].MemberUserIds = ["test-user", second.Id];
        f.Config.Vaults = [f.Config.Vaults[0], new() { Id = "second-vault", OfflineEnabled = true, MemberUserIds = ["test-user", second.Id] }];
        f.Config.Devices[0].UserIds = ["test-user", second.Id];
        f.Config.Devices[0].VaultIds = ["synthetic", "second-vault"];
        var first = f.Service().Sync(f.Request(), f.Client);
        Assert.False(first.Denied); Assert.Equal(4, first.Packages.Length);
        switch (change)
        {
            case "disabled": second.Enabled = false; break;
            case "removed-user":
                f.Config.Users = [f.Config.Users[0]];
                f.Config.Devices[0].UserIds = ["test-user"];
                foreach (var vault in f.Config.Vaults) vault.MemberUserIds = ["test-user"];
                break;
            case "removed-membership":
                f.Config.Devices[0].UserIds = ["test-user"];
                foreach (var vault in f.Config.Vaults) vault.MemberUserIds = ["test-user"];
                break;
            case "removed-vault-assignment": f.Config.Devices[0].VaultIds = ["synthetic"]; break;
            case "lost-reveal": second.Roles = [ArkanumRole.Auditor]; break;
        }
        var q = f.Request(); var denied = f.Service().Sync(q, f.Client);
        Assert.True(denied.Denied); Assert.Empty(denied.Packages);
        Assert.True(new ProofResponseVerifier(f.Signer.ExportSubjectPublicKeyInfo()).Accept(denied, q, first.Sequence, false));
        second.Enabled = true; second.Roles = [ArkanumRole.User];
        Assert.True(f.Service().Sync(f.Request(), f.Client).Denied);
    }

    [Fact]
    public void Encrypted_utf8_json_contains_test_credential_and_secure_note_with_verified_hash_only()
    {
        using var f = new Fixture(); var p = Assert.Single(f.Service().Sync(f.Request(), f.Client).Packages);
        var key = f.Unwrap.Decrypt(p.WrappedKey, RSAEncryptionPadding.OaepSHA256);
        var plain = new byte[p.Snapshot.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(p.Snapshot.Nonce, p.Snapshot.Ciphertext, p.Snapshot.Tag, plain, GrantCanonical.Encode(p.Grant.Claims));
            Assert.True(Convert.ToHexString(SHA256.HashData(plain)) == p.Grant.Claims.ContentHash);
            using var json = JsonDocument.Parse(plain);
            Assert.True(json.RootElement.GetProperty("testData").GetBoolean());
            var credential = json.RootElement.GetProperty("credentials")[0];
            var note = json.RootElement.GetProperty("secureNotes")[0];
            Assert.True(!string.IsNullOrEmpty(credential.GetProperty("username").GetString()));
            Assert.True(!string.IsNullOrEmpty(credential.GetProperty("password").GetString()));
            Assert.True(!string.IsNullOrEmpty(note.GetProperty("text").GetString()));
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    [Fact]
    public async Task Real_https_issues_signed_90_second_lease_and_actual_elapsed_time_expires_it_without_refresh()
    {
        using var f = new Fixture(); var config = Load(f, 90);
        using var trust = new TemporaryTrust(f.Server, f.Client);
        await using var app = ProofHost.Build(config, f.Server, f.Signer, 0); await app.StartAsync();
        try
        {
            using var handler = new HttpClientHandler(); handler.ClientCertificates.Add(f.Client);
            using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single().Replace("127.0.0.1", "localhost")) };
            var q = f.Request(); var result = await client.PostAsJsonAsync("/sprint1/sync", q); result.EnsureSuccessStatusCode();
            var response = (await result.Content.ReadFromJsonAsync<ProofSyncResponse>())!;
            Assert.True(new ProofResponseVerifier(f.Signer.ExportSubjectPublicKeyInfo()).Accept(response, q, 0, false));
            var p = Assert.Single(response.Packages); var claims = p.Grant.Claims;
            Assert.Equal(TimeSpan.FromSeconds(90), claims.ExpiresAt - claims.IssuedAt);
            Assert.Equal(response.ServerUtc, claims.IssuedAt);
            var key = f.Unwrap.Decrypt(p.WrappedKey, RSAEncryptionPadding.OaepSHA256);
            try
            {
                using var reader = new SnapshotProofReader(f.Signer.ExportSubjectPublicKeyInfo());
                var context = new OpenContext(claims.UserSid, claims.DeviceId, claims.VaultId, claims.ContentVersion, claims.Revision, claims.DenyWatermark);
                var state = new LeaseState("synthetic-test-boot", claims.IssuedAt, TimeSpan.Zero, claims.IssuedAt, TimeSpan.Zero);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var opened = reader.Open(p.Grant, p.Snapshot, key, context, state, new("synthetic-test-boot", DateTimeOffset.UtcNow, timer.Elapsed));
                Assert.NotNull(opened.Plaintext); CryptographicOperations.ZeroMemory(opened.Plaintext!);
                var originalExpiry = claims.ExpiresAt;
                await Task.Delay(claims.ExpiresAt - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(250));
                var expired = reader.Open(p.Grant, p.Snapshot, key, context, opened.NextState, new("synthetic-test-boot", DateTimeOffset.UtcNow, timer.Elapsed));
                Assert.Null(expired.Plaintext); Assert.Equal(originalExpiry, p.Grant.Claims.ExpiresAt);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        finally { await app.StopAsync(); }
    }
}
