using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Arkanum.Go.Proof.Protocol;
using Arkanum.Go.Proof.Server;
using Arkanum.Web.Services;

namespace Arkanum.Go.Proof.Tests;

public sealed class BoundaryTests
{
    [Fact]
    public void Changing_issue_time_cannot_extend_an_existing_enrollment()
    {
        using var f = new Fixture(); var before = f.Service().Sync(f.Request(), f.Client);
        f.Config.Devices[0].IssuedAt = DateTimeOffset.UtcNow;
        var after = f.Service().Sync(f.Request(), f.Client);
        Assert.True(after.Denied); Assert.Empty(after.Packages); Assert.True(after.Sequence > before.Sequence);
    }
    [Fact]
    public void Separate_unwrap_key_and_unique_device_authentication_pin_are_required()
    {
        using var f = new Fixture(); using var auth = f.Client.GetRSAPublicKey();
        f.Config.Devices[0].UnwrapSpki = Convert.ToBase64String(auth!.ExportSubjectPublicKeyInfo());
        Assert.Throws<UnauthorizedAccessException>(() => f.Service().Sync(f.Request(), f.Client));
        f.Config.Devices[0].UnwrapSpki = Convert.ToBase64String(f.Unwrap.ExportSubjectPublicKeyInfo());
        f.Config.Devices = [f.Config.Devices[0], new() { Id = "duplicate", CertificateSha256 = f.Config.Devices[0].CertificateSha256 }];
        Assert.Throws<UnauthorizedAccessException>(() => f.Service().Sync(f.Request(), f.Client));
    }
    [Fact]
    public void Registry_rejects_ambiguous_directory_identifiers()
    {
        using var f = new Fixture(); var original = f.Config.Users[0];
        f.Config.Users = [original, new() { Id = "duplicate-user", Sid = original.Sid, ObjectGuid = original.ObjectGuid }];
        Assert.True(f.Service().Sync(f.Request(), f.Client).Denied);
    }
    [Fact]
    public void Unwrap_public_key_cannot_be_shared_by_two_devices()
    {
        using var f = new Fixture(); using var other = Fixture.Cert("different-device", false);
        f.Config.Devices = [f.Config.Devices[0], new() { Id = "other", CertificateSha256 = other.GetCertHashString(HashAlgorithmName.SHA256), UnwrapSpki = f.Config.Devices[0].UnwrapSpki }];
        Assert.Throws<UnauthorizedAccessException>(() => f.Service().Sync(f.Request(), f.Client));
    }
    [Fact]
    public void Every_package_byte_and_grant_signature_are_response_authenticated()
    {
        using var f = new Fixture(); var q = f.Request(); var r = f.Service().Sync(q, f.Client); var p = r.Packages.Single();
        var verifier = new ProofResponseVerifier(f.Signer.ExportSubjectPublicKeyInfo());
        foreach (var modified in new[]{
            p with{WrappedKey=[1]}, p with{Snapshot=p.Snapshot with{Ciphertext=[1]}},
            p with{Snapshot=p.Snapshot with{Tag=[1]}},p with{Snapshot=p.Snapshot with{Nonce=[1]}},
            p with{Grant=p.Grant with{Signature=[1]}},p with{Grant=p.Grant with{Claims=p.Grant.Claims with{ExpiresAt=p.Grant.Claims.ExpiresAt.AddHours(1)}}}})
            Assert.False(verifier.Accept(r with { Packages = [modified] }, q, 0, false));
    }
    [Fact]
    public async Task Device_certificate_is_not_a_portal_login_on_actual_Arkanum_Web_endpoints()
    {
        using var f = new Fixture(); using var trust = new TemporaryTrust(f.Server, f.Client);
        Directory.CreateDirectory(f.Config.StateDirectory);
        var pfx = Path.Combine(f.Config.StateDirectory, "server.pfx");
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await File.WriteAllBytesAsync(pfx, f.Server.Export(X509ContentType.Pfx, password));
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var assembly = typeof(VaultAuthorizationService).Assembly.Location;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("PROOF_DOTNET") ?? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", assembly)
        { WorkingDirectory = f.Config.StateDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // The unchanged portal must never inherit a real DB connection, key-file path,
        // directory endpoint or configured listener from the operator's environment.
        foreach (var key in start.Environment.Keys.Where(k =>
            k.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("ARKANUM", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("DOTNET_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        foreach (var entry in new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Testing",
            ["ASPNETCORE_CONTENTROOT"] = f.Config.StateDirectory,
            ["ASPNETCORE_Kestrel__Endpoints__Proof__Url"] = $"https://localhost:{port}",
            ["ASPNETCORE_Kestrel__Endpoints__Proof__Certificate__Path"] = pfx,
            ["ASPNETCORE_Kestrel__Endpoints__Proof__Certificate__Password"] = password,
            ["ASPNETCORE_Kestrel__Endpoints__Proof__ClientCertificateMode"] = "RequireCertificate",
            ["ARKANUM_MASTER_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }) start.Environment[entry.Key] = entry.Value;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false }; handler.ClientCertificates.Add(f.Client);
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{port}"), Timeout = TimeSpan.FromSeconds(3) };
            var ready = false;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (process.HasExited) throw new Exception(await stdout + await stderr);
                try { ready = (await client.GetAsync("/health")).IsSuccessStatusCode; if (ready) break; }
                catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            Assert.True(ready, "Disposable real portal did not become ready.");
            foreach (var path in new[] { "/Users", "/Settings", "/Vaults", "/Vaults/Details/00000000-0000-0000-0000-000000000001", "/health/details", "/install-report" })
            {
                var response = await client.GetAsync(path);
                Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"{path}: {response.StatusCode}");
            }
            foreach (var path in new[] { "/Users?handler=CreateLocal", "/Settings?handler=UpdateUserRoles", "/Vaults/Details/00000000-0000-0000-0000-000000000001?handler=EditCredential", "/Vaults/Details/00000000-0000-0000-0000-000000000001?handler=CreateLogin", "/Vaults/Details/00000000-0000-0000-0000-000000000001?handler=ExportVaultCsv" })
            {
                var response = await client.PostAsync(path, new FormUrlEncodedContent([]));
                Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Redirect or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"{path}: {response.StatusCode}");
            }
            using var anonymous = new HttpClient { BaseAddress = client.BaseAddress };
            await Assert.ThrowsAsync<HttpRequestException>(() => anonymous.GetAsync("/health"));
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
