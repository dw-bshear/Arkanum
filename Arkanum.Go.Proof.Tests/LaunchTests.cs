using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arkanum.Go.Proof.Protocol;
using Arkanum.Go.Proof.Server;
namespace Arkanum.Go.Proof.Tests;

public sealed class LaunchTests
{
    [Fact]
    public void File_registry_requires_absolute_state_path_and_unique_identifiers()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.Config.StateDirectory);
        var path = Path.Combine(f.Config.StateDirectory, "registry.json");
        File.WriteAllText(path, JsonSerializer.Serialize(f.Config));
        Assert.Equal("test-device", ProofServerProgram.LoadConfiguration(path).Devices.Single().Id);
        f.Config.StateDirectory = "relative-state"; File.WriteAllText(path, JsonSerializer.Serialize(f.Config));
        Assert.Throws<InvalidDataException>(() => ProofServerProgram.LoadConfiguration(path));
        f.Config.StateDirectory = Path.GetDirectoryName(path)!; f.Config.Devices = [f.Config.Devices[0], f.Config.Devices[0]];
        File.WriteAllText(path, JsonSerializer.Serialize(f.Config));
        Assert.Throws<InvalidDataException>(() => ProofServerProgram.LoadConfiguration(path));
    }
    [Fact]
    public async Task Runnable_server_loads_filebacked_secrets_and_serves_real_mtls()
    {
        using var f = new Fixture(); using var trust = new TemporaryTrust(f.Server, f.Client);
        Directory.CreateDirectory(f.Config.StateDirectory);
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var registry = Path.Combine(f.Config.StateDirectory, "registry.json");
        f.Config.Port = port;
        await File.WriteAllTextAsync(registry, JsonSerializer.Serialize(f.Config, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }));
        var pfx = Path.Combine(f.Config.StateDirectory, "server.pfx"); var password = Path.Combine(f.Config.StateDirectory, "password.txt"); var signing = Path.Combine(f.Config.StateDirectory, "signer.pem");
        await File.WriteAllBytesAsync(pfx, f.Server.Export(X509ContentType.Pfx, "test-only-disposable"));
        await File.WriteAllTextAsync(password, "test-only-disposable"); await File.WriteAllTextAsync(signing, f.Signer.ExportPkcs8PrivateKeyPem());
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("PROOF_DOTNET") ?? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", typeof(ProofService).Assembly.Location)
        { WorkingDirectory = f.Config.StateDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var pair in new Dictionary<string, string> { ["PROOF_REGISTRY_FILE"] = registry, ["PROOF_TLS_PFX_FILE"] = pfx, ["PROOF_TLS_PASSWORD_FILE"] = password, ["PROOF_SIGNING_KEY_FILE"] = signing }) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var handler = new HttpClientHandler(); handler.ClientCertificates.Add(f.Client);
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{port}"), Timeout = TimeSpan.FromSeconds(3) };
            ProofSyncResponse? response = null; ProofSyncRequest request = f.Request();
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (process.HasExited) throw new Exception(await stdout + await stderr);
                try { var result = await client.PostAsJsonAsync("/sprint1/sync", request); result.EnsureSuccessStatusCode(); response = await result.Content.ReadFromJsonAsync<ProofSyncResponse>(); break; }
                catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            Assert.NotNull(response); Assert.False(response.Denied); Assert.True(new ProofResponseVerifier(f.Signer.ExportSubjectPublicKeyInfo()).Accept(response, request, 0, false));
        }
        finally
        {
            if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
