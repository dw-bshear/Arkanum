using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Arkanum.Go.Core;
using Arkanum.Go.Proof.Protocol;
using Arkanum.Go.Proof.Server;
using Arkanum.Web.Domain;

namespace Arkanum.Go.Proof.Tests;

public sealed class ProofTests
{
    [Fact]
    public void Wire_is_deterministic_and_binds_every_field()
    {
        var r = new ProofSyncResponse(ProofWire.ProtocolId, "device", "nonce", 1, DateTimeOffset.UtcNow, [], true, []);
        Assert.Equal(ProofWire.EncodeUnsigned(r), ProofWire.EncodeUnsigned(r with { Signature = [1] }));
        foreach (var changed in new[] { r with { Nonce = "other" }, r with { DeviceId = "other" }, r with { Sequence = 2 }, r with { Denied = false }, r with { ServerUtc = r.ServerUtc.AddTicks(1) }, r with { Protocol = "other" } })
            Assert.NotEqual(ProofWire.EncodeUnsigned(r), ProofWire.EncodeUnsigned(changed));
    }
    [Fact]
    public void Signed_responses_reject_old_nonce_sequence_tampering_and_latch_deny()
    {
        using var f = new Fixture(); var service = f.Service();
        var request = f.Request(); var response = service.Sync(request, f.Client);
        var verifier = new ProofResponseVerifier(f.Signer.ExportSubjectPublicKeyInfo());
        Assert.True(verifier.Accept(response, request, 0, false));
        Assert.False(verifier.Accept(response, request, response.Sequence, false));
        Assert.False(verifier.Accept(response, request with { Nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }, 0, false));
        Assert.False(verifier.Accept(response with { Denied = true }, request, 0, false));
        Assert.False(verifier.Accept(response, request, 0, true));
        f.Config.Devices[0].Approved = false;
        var denied = service.Sync(f.Request(), f.Client); Assert.True(denied.Denied); Assert.Empty(denied.Packages);
        Assert.True(verifier.Accept(denied, new(denied.DeviceId, denied.Nonce), response.Sequence, false));
        f.Config.Devices[0].Approved = true;
        Assert.True(service.Sync(f.Request(), f.Client).Denied);
        Assert.True(f.Service().Sync(f.Request(), f.Client).Denied);
    }
    [Fact]
    public void Packages_use_actual_policy_explicit_membership_and_authenticated_encryption()
    {
        using var f = new Fixture(); var svc = f.Service(); var first = svc.Sync(f.Request(), f.Client);
        Assert.False(first.Denied); var p = Assert.Single(first.Packages);
        Assert.Equal(first.ServerUtc, p.Grant.Claims.IssuedAt);
        Assert.Equal(TimeSpan.FromHours(48), p.Grant.Claims.ExpiresAt - p.Grant.Claims.IssuedAt);
        Assert.True(Assert.Single(svc.Sync(f.Request(), f.Client).Packages).Grant.Claims.ExpiresAt > p.Grant.Claims.ExpiresAt);
        Assert.True(f.Signer.VerifyData(GrantCanonical.Encode(p.Grant.Claims), p.Grant.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        var key = f.Unwrap.Decrypt(p.WrappedKey, RSAEncryptionPadding.OaepSHA256); var plain = new byte[p.Snapshot.Ciphertext.Length];
        using var aes = new AesGcm(key, 16); aes.Decrypt(p.Snapshot.Nonce, p.Snapshot.Ciphertext, p.Snapshot.Tag, plain, GrantCanonical.Encode(p.Grant.Claims));
        Assert.True(CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(ProofService.SyntheticPayload)), SHA256.HashData(plain)));
        using var reader = new SnapshotProofReader(f.Signer.ExportSubjectPublicKeyInfo());
        var claims = p.Grant.Claims;
        var state = new LeaseState("test-boot", claims.IssuedAt, TimeSpan.Zero, claims.IssuedAt, TimeSpan.Zero);
        var context = new OpenContext(claims.UserSid, claims.DeviceId, claims.VaultId, claims.ContentVersion, claims.Revision, claims.DenyWatermark);
        var opened = reader.Open(p.Grant, p.Snapshot, key, context, state, new("test-boot", claims.IssuedAt.AddHours(47), TimeSpan.FromHours(47)));
        Assert.NotNull(opened.Plaintext);
        var expired = reader.Open(p.Grant, p.Snapshot, key, context, opened.NextState, new("test-boot", claims.ExpiresAt, TimeSpan.FromHours(48)));
        Assert.Null(expired.Plaintext);
        Assert.Null(reader.Open(p.Grant, p.Snapshot, key, context, expired.NextState, new("test-boot", claims.IssuedAt.AddHours(1), TimeSpan.FromHours(49))).Plaintext);
        Assert.Throws<AuthenticationTagMismatchException>(() => aes.Decrypt(p.Snapshot.Nonce, p.Snapshot.Ciphertext, p.Snapshot.Tag, plain, GrantCanonical.Encode(p.Grant.Claims with { VaultId = "other" })));
    }
    [Theory]
    [InlineData("approval")]
    [InlineData("membership")]
    [InlineData("assignment")]
    [InlineData("offline")]
    [InlineData("auditor")]
    [InlineData("disabled")]
    [InlineData("future-enrollment")]
    [InlineData("provenance")]
    public void Missing_authorization_produces_complete_signed_deny(string condition)
    {
        using var f = new Fixture();
        switch (condition)
        {
            case "approval": f.Config.Devices[0].Approved = false; break;
            case "membership": f.Config.Vaults[0].MemberUserIds = []; break;
            case "assignment": f.Config.Devices[0].VaultIds = []; break;
            case "offline": f.Config.Vaults[0].OfflineEnabled = false; break;
            case "auditor": f.Config.Users[0].Roles = [ArkanumRole.Auditor]; break;
            case "disabled": f.Config.Users[0].Enabled = false; break;
            case "future-enrollment": f.Config.Devices[0].IssuedAt = DateTimeOffset.UtcNow.AddHours(1); break;
            case "provenance": f.Config.Users[0].VerifiedBy = ""; break;
        }
        var q = f.Request(); var r = f.Service().Sync(q, f.Client);
        Assert.True(r.Denied); Assert.Empty(r.Packages); Assert.True(new ProofResponseVerifier(f.Signer.ExportSubjectPublicKeyInfo()).Accept(r, q, 0, false));
    }
    [Fact]
    public void Wrong_certificate_cannot_claim_approved_device()
    {
        using var f = new Fixture(); using var other = Fixture.Cert("other", false);
        Assert.Throws<UnauthorizedAccessException>(() => f.Service().Sync(f.Request(), other));
        Assert.Throws<UnauthorizedAccessException>(() => f.Service().Sync(f.Request() with { DeviceId = "unknown" }, f.Client));
        Assert.Throws<ArgumentException>(() => f.Service().Sync(f.Request() with { Nonce = "bad" }, f.Client));
    }
    [Fact]
    public async Task Real_https_requires_client_certificate_and_never_maps_device_to_web_user()
    {
        using var f = new Fixture();
        await using var app = ProofHost.Build(f.Config, f.Server, f.Signer, 0); await app.StartAsync();
        using var wrong = Fixture.Cert("wrong", false);
        using var trust = new TemporaryTrust(f.Server, f.Client, wrong);
        var address = app.Urls.Single().Replace("127.0.0.1", "localhost");
        using var mismatch = new HttpClient();
        await Assert.ThrowsAsync<HttpRequestException>(() => mismatch.GetAsync(app.Urls.Single()));
        using var handler = new HttpClientHandler(); handler.ClientCertificates.Add(f.Client);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        var q = f.Request(); var result = await client.PostAsJsonAsync("/sprint1/sync", q);
        result.EnsureSuccessStatusCode(); var r = await result.Content.ReadFromJsonAsync<ProofSyncResponse>(); Assert.False(r!.Denied);
        f.Config.Devices[0].Approved = false;
        var deniedRequest = f.Request(); var deniedResult = await client.PostAsJsonAsync("/sprint1/sync", deniedRequest);
        deniedResult.EnsureSuccessStatusCode(); var denied = await deniedResult.Content.ReadFromJsonAsync<ProofSyncResponse>();
        Assert.True(denied!.Denied); Assert.Empty(denied.Packages);
        Assert.True(new ProofResponseVerifier(f.Signer.ExportSubjectPublicKeyInfo()).Accept(denied, deniedRequest, r.Sequence, false));
        foreach (var path in new[] { "/Users", "/Settings", "/Vaults", "/Vaults/Edit", "/health/details", "/install-report" })
        {
            var routeDenied = await client.GetAsync(path); Assert.NotEqual(HttpStatusCode.OK, routeDenied.StatusCode);
        }
        using var anonymous = new HttpClient() { BaseAddress = new Uri(address) };
        await Assert.ThrowsAsync<HttpRequestException>(() => anonymous.PostAsJsonAsync("/sprint1/sync", q));
        using var wrongHandler = new HttpClientHandler(); wrongHandler.ClientCertificates.Add(wrong);
        using var wrongClient = new HttpClient(wrongHandler) { BaseAddress = new Uri(address) };
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongClient.PostAsJsonAsync("/sprint1/sync", q)).StatusCode);
        await app.StopAsync();
    }
}
public sealed class Fixture : IDisposable
{
    public RSA Signer = RSA.Create(2048), Unwrap = RSA.Create(2048);
    public X509Certificate2 Client = Cert("client", false), Server = Cert("localhost", true);
    public ProofConfiguration Config;
    public Fixture() { Config = new() { StateDirectory = Path.Combine(Path.GetTempPath(), "arkanum-proof-" + Guid.NewGuid()), Users = [new() { Id = "test-user", Sid = "S-1-5-21-101-202-303-1001", DomainSid = "S-1-5-21-101-202-303", ObjectGuid = Guid.NewGuid(), Domain = "test.invalid", VerifiedBy = "automated-synthetic-test-only", VerifiedAt = DateTimeOffset.UtcNow, Enabled = true, Roles = [ArkanumRole.User] }], Vaults = [new() { Id = "synthetic", OfflineEnabled = true, MemberUserIds = ["test-user"] }], Devices = [new() { Id = "test-device", Approved = true, CertificateSha256 = Client.GetCertHashString(HashAlgorithmName.SHA256), UnwrapSpki = Convert.ToBase64String(Unwrap.ExportSubjectPublicKeyInfo()), IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-1), UserIds = ["test-user"], VaultIds = ["synthetic"] }] }; Config.EnrollmentDirectory = Config.StateDirectory + "-enrollments"; }
    public ProofService Service() => new(Config, Signer);
    public ProofSyncRequest Request() => new("test-device", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    public static X509Certificate2 Cert(string name, bool server) => TestCertificates.Create(name, server);
    public void Dispose() { Signer.Dispose(); Unwrap.Dispose(); Client.Dispose(); Server.Dispose(); if (Directory.Exists(Config.StateDirectory)) Directory.Delete(Config.StateDirectory, true); if (Directory.Exists(Config.EnrollmentDirectory)) Directory.Delete(Config.EnrollmentDirectory, true); }
}
