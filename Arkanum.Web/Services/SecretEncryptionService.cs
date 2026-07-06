using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Arkanum.Web.Services;

public interface IMasterKeyProvider
{
    byte[] GetMasterKey();
    string KeyId { get; }
}

public sealed class StaticMasterKeyProvider(string keyMaterial) : IMasterKeyProvider
{
    public string KeyId { get; init; } = "env:v1";
    public byte[] GetMasterKey() => NormalizeKey(keyMaterial);

    public static byte[] NormalizeKey(string keyMaterial)
    {
        if (string.IsNullOrWhiteSpace(keyMaterial)) throw new InvalidOperationException("Master key material is required.");
        return keyMaterial.Length == 32
            ? Encoding.UTF8.GetBytes(keyMaterial)
            : SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
    }
}

public sealed class EnvironmentMasterKeyProvider(IConfiguration configuration) : IMasterKeyProvider
{
    public string KeyId => "site:v1";

    public byte[] GetMasterKey()
    {
        var envValue = GetEnvironmentKey(configuration);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return StaticMasterKeyProvider.NormalizeKey(envValue);
        }

        var keyFile = ResolveMasterKeyFilePath(configuration);
        if (File.Exists(keyFile))
        {
            var fileValue = File.ReadAllText(keyFile).Trim();
            if (!string.IsNullOrWhiteSpace(fileValue))
            {
                return StaticMasterKeyProvider.NormalizeKey(fileValue);
            }
        }

        var variable = configuration["Arkanum:EncryptionKeyEnvironmentVariable"] ?? "ARKANUM_MASTER_KEY";
        throw new InvalidOperationException($"Master encryption key is not set. Set environment variable '{variable}' or create the configured Arkanum master key file.");
    }

    public static string? EnsureLocalMasterKey(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(GetEnvironmentKey(configuration)))
        {
            return null;
        }

        var keyFile = ResolveMasterKeyFilePath(configuration);
        if (File.Exists(keyFile) && !string.IsNullOrWhiteSpace(File.ReadAllText(keyFile)))
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(keyFile) ?? ".");
        var recoveryKey = SiteRecoveryKeyService.GenerateRecoveryKey();
        File.WriteAllText(keyFile, recoveryKey + Environment.NewLine);
        TrySetOwnerOnlyPermissions(keyFile);
        return recoveryKey;
    }

    public static string? TryReadConfiguredRecoveryKey(IConfiguration configuration)
    {
        var envValue = GetEnvironmentKey(configuration);
        if (SiteRecoveryKeyService.TryValidateRecoveryKeyFormat(envValue ?? string.Empty, out _))
        {
            return envValue!.Trim();
        }

        var keyFile = ResolveMasterKeyFilePath(configuration);
        if (!File.Exists(keyFile)) return null;

        var fileValue = File.ReadAllText(keyFile).Trim();
        return SiteRecoveryKeyService.TryValidateRecoveryKeyFormat(fileValue, out _) ? fileValue : null;
    }

    public static string ResolveMasterKeyFilePath(IConfiguration configuration)
    {
        var configured = configuration["Arkanum:MasterKeyFile"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var sqliteConnectionString = configuration.GetConnectionString("SqliteFallback") ?? "Data Source=/srv/blackbox/hermes/projects/arkanum/data/arkanum-dev.db";
        var sqlitePath = sqliteConnectionString.Replace("Data Source=", string.Empty, StringComparison.OrdinalIgnoreCase);
        var directory = Path.GetDirectoryName(sqlitePath);
        return Path.Combine(string.IsNullOrWhiteSpace(directory) ? AppContext.BaseDirectory : directory, "arkanum-site-recovery.key");
    }

    private static string? GetEnvironmentKey(IConfiguration configuration)
    {
        var variable = configuration["Arkanum:EncryptionKeyEnvironmentVariable"] ?? "ARKANUM_MASTER_KEY";
        return Environment.GetEnvironmentVariable(variable);
    }

    private static void TrySetOwnerOnlyPermissions(string keyFile)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Permission hardening is best-effort because Windows/IIS installs use ACLs instead.
        }
    }
}

public sealed record EncryptedSecretPayload(string Ciphertext, string KeyId, string Algorithm);

public sealed class SecretEncryptionService(IMasterKeyProvider keyProvider)
{
    public EncryptedSecretPayload Encrypt(string plaintext, string? keyId = null)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(keyProvider.GetMasterKey(), 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        var envelope = string.Join('.', Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(cipherBytes));
        return new EncryptedSecretPayload(envelope, keyId ?? keyProvider.KeyId, "AES-256-GCM");
    }

    public string Decrypt(EncryptedSecretPayload payload)
    {
        var parts = payload.Ciphertext.Split('.');
        if (parts.Length != 3) throw new InvalidOperationException("Encrypted secret payload is malformed.");
        var nonce = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var cipherBytes = Convert.FromBase64String(parts[2]);
        var plainBytes = new byte[cipherBytes.Length];
        using var aes = new AesGcm(keyProvider.GetMasterKey(), 16);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
