using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arkanum.Go.Core;

namespace Arkanum.Go.Proof.Server;

public static class ProofServerProgram
{
    public static async Task Main()
    {
        var config = LoadConfiguration(RequiredFile("PROOF_REGISTRY_FILE"));
        var password = File.ReadAllText(RequiredFile("PROOF_TLS_PASSWORD_FILE")).TrimEnd('\r', '\n');
        using var tls = X509CertificateLoader.LoadPkcs12FromFile(RequiredFile("PROOF_TLS_PFX_FILE"), password,
            X509KeyStorageFlags.EphemeralKeySet);
        using var signer = RSA.Create();
        signer.ImportFromPem(File.ReadAllText(RequiredFile("PROOF_SIGNING_KEY_FILE")));
        if (signer.KeySize < 2048) throw new InvalidDataException("RSA signing key must be at least 2048 bits.");
        await using var app = ProofHost.Build(config, tls, signer, config.Port, IPAddress.Parse(config.ListenAddress));
        await app.RunAsync();
    }

    public static ProofConfiguration LoadConfiguration(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var config = JsonSerializer.Deserialize<ProofConfiguration>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("Empty proof registry.");
        config.ValidateLease();
        config.ValidateEnrollmentDirectory();
        if (string.IsNullOrWhiteSpace(config.StateDirectory) || !Path.IsPathFullyQualified(config.StateDirectory) ||
            config.Port is < 1 or > 65535 || !IPAddress.TryParse(config.ListenAddress, out _) ||
            config.Devices is null || config.Users is null || config.Vaults is null ||
            config.Devices.Length > 1000 || config.Users.Length > 1000 || config.Vaults.Length > 1000)
            throw new InvalidDataException("Invalid proof registry boundaries.");
        Unique(config.Devices.Select(d => d.Id)); Unique(config.Users.Select(u => u.Id)); Unique(config.Vaults.Select(v => v.Id));
        Unique(config.Devices.Select(d => d.CertificateSha256)); Unique(config.Users.Select(u => u.Sid));
        Unique(config.Devices.Select(d => d.UnwrapSpki));
        Unique(config.Users.Select(u => u.ObjectGuid.ToString("D")));
        foreach (var user in config.Users)
        {
            AdSid.ParseUser(user.Sid, user.DomainSid);
            if (user.ObjectGuid == Guid.Empty || string.IsNullOrWhiteSpace(user.Domain) || string.IsNullOrWhiteSpace(user.VerifiedBy) ||
                user.VerifiedAt == default || user.VerifiedAt > DateTimeOffset.UtcNow || user.Roles is null || user.Roles.Any(r => !Enum.IsDefined(r)))
                throw new InvalidDataException("Directory provenance is required for every registry user.");
        }
        foreach (var device in config.Devices)
        {
            if (device.CertificateSha256.Length != 64 || device.CertificateSha256.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')) ||
                device.VaultIds is null || device.IssuedAt == default || device.VaultIds.Length > 100)
                throw new InvalidDataException("Invalid device approval record.");
            using var unwrap = RSA.Create();
            var spki = Convert.FromBase64String(device.UnwrapSpki); unwrap.ImportSubjectPublicKeyInfo(spki, out var count);
            if (count != spki.Length || unwrap.KeySize < 2048 || device.UnwrapSpki != Convert.ToBase64String(unwrap.ExportSubjectPublicKeyInfo()))
                throw new InvalidDataException("Invalid unwrap key pin.");
        }
        if (config.Vaults.Any(v => v.MemberUserIds is null)) throw new InvalidDataException("Explicit membership arrays required.");
        return config;
    }

    private static void Unique(IEnumerable<string> ids)
    {
        var list = ids.ToArray();
        if (list.Any(string.IsNullOrWhiteSpace) || list.Distinct(StringComparer.Ordinal).Count() != list.Length)
            throw new InvalidDataException("Registry identifiers and pins must be unique and nonempty.");
    }
    private static string RequiredFile(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new InvalidDataException($"{name} must name an existing absolute file path.");
        return path;
    }
}
