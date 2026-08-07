using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public sealed class Issue023VaultExportAuthenticatedEncryptionTests
{
    [Fact]
    public async Task ExportPackageUsesAesGcmManifestAndValidationUtilityDecryptsCsv()
    {
        await using var db = CreateDb();
        var (vault, crypto) = await SeedVaultWithSecretAsync(db, "PlainSecret!2026");
        var service = new VaultExportService(crypto);

        var export = await service.ExportVaultCsvZipAsync(db, vault.Id, "admin", "ExportPassphrase!2026", DateTimeOffset.Parse("2026-08-07T10:00:00Z"));

        using var zip = new ZipArchive(new MemoryStream(export.Content), ZipArchiveMode.Read);
        var manifest = JsonDocument.Parse(ReadEntry(zip, "manifest.json")).RootElement;
        Assert.Equal("AES-256-GCM/PBKDF2-SHA256", manifest.GetProperty("encryption").GetString());
        Assert.Equal(1, manifest.GetProperty("encryptionVersion").GetInt32());
        Assert.Equal("credentials.csv.aesgcm", manifest.GetProperty("encryptedEntry").GetString());
        Assert.Equal("AES-256-GCM/PBKDF2-SHA256", manifest.GetProperty("integrity").GetProperty("algorithm").GetString());
        Assert.True(manifest.GetProperty("integrity").GetProperty("tagBytes").GetInt32() >= 16);
        Assert.DoesNotContain(zip.Entries, e => e.FullName == "credentials.csv.aes");
        Assert.Contains(zip.Entries, e => e.FullName == "credentials.csv.aesgcm");

        var csv = VaultExportService.ValidateAndDecryptExportCsvZip(export.Content, "ExportPassphrase!2026");

        Assert.Contains("Firewall admin", csv);
        Assert.Contains("PlainSecret!2026", csv);
        Assert.Contains("VaultName,FolderPath,ItemType,Title,Username,SecretValue", csv);
    }

    [Fact]
    public async Task ExportValidationUtilityRejectsTamperedPayload()
    {
        await using var db = CreateDb();
        var (vault, crypto) = await SeedVaultWithSecretAsync(db, "PlainSecret!2026");
        var service = new VaultExportService(crypto);
        var export = await service.ExportVaultCsvZipAsync(db, vault.Id, "admin", "ExportPassphrase!2026", DateTimeOffset.Parse("2026-08-07T10:00:00Z"));
        var tampered = ReplaceZipEntry(export.Content, "credentials.csv.aesgcm", payload =>
        {
            var json = JsonDocument.Parse(payload).RootElement;
            var cipher = Convert.FromBase64String(json.GetProperty("ciphertext").GetString()!);
            cipher[^1] ^= 0x41;
            return JsonSerializer.Serialize(new
            {
                algorithm = json.GetProperty("algorithm").GetString(),
                version = json.GetProperty("version").GetInt32(),
                kdf = json.GetProperty("kdf").GetString(),
                iterations = json.GetProperty("iterations").GetInt32(),
                salt = json.GetProperty("salt").GetString(),
                nonce = json.GetProperty("nonce").GetString(),
                tag = json.GetProperty("tag").GetString(),
                ciphertext = Convert.ToBase64String(cipher)
            });
        });

        var ex = Assert.Throws<InvalidOperationException>(() => VaultExportService.ValidateAndDecryptExportCsvZip(tampered, "ExportPassphrase!2026"));
        Assert.Contains("integrity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportValidationDocumentationDescribesTamperCheckPath()
    {
        var root = ProjectRoot();
        var docs = File.ReadAllText(Path.Combine(root, "docs", "vault-export-validation.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("ValidateAndDecryptExportCsvZip", docs);
        Assert.Contains("AES-256-GCM", docs);
        Assert.Contains("tamper", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vault-export-validation.md", readme);
    }

    private static async Task<(VaultEntity Vault, SecretEncryptionService Crypto)> SeedVaultWithSecretAsync(ArkanumDbContext db, string secret)
    {
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.Parse("2026-08-07T09:00:00Z");
        var vault = new VaultEntity { Name = "Ops", Description = "Ops", CreatedByUserKey = "owner", CreatedAt = now };
        var folder = new VaultFolderEntity { VaultId = vault.Id, Name = "General", CreatedAt = now };
        db.Vaults.Add(vault);
        db.VaultFolders.Add(folder);
        var crypto = new SecretEncryptionService(new StaticMasterKeyProvider("issue023-vault-export-key"));
        var encrypted = crypto.Encrypt(secret);
        db.VaultItems.Add(new VaultItemEntity
        {
            VaultId = vault.Id,
            FolderId = folder.Id,
            Type = VaultItemType.LoginCredential,
            Title = "Firewall admin",
            Username = "fwadmin",
            EncryptedSecret = encrypted.Ciphertext,
            EncryptionKeyId = encrypted.KeyId,
            EncryptionAlgorithm = encrypted.Algorithm,
            ResetNotificationDate = new DateOnly(2026, 8, 1)
        });
        await db.SaveChangesAsync();
        return (vault, crypto);
    }

    private static ArkanumDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("n"))
            .Options;
        return new ArkanumDbContext(options);
    }

    private static string ReadEntry(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"Missing {entryName}.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReplaceZipEntry(byte[] original, string entryName, Func<string, string> replace)
    {
        using var output = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(original), ZipArchiveMode.Read))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var sourceEntry in source.Entries)
            {
                var targetEntry = target.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                using var targetStream = targetEntry.Open();
                if (sourceEntry.FullName == entryName)
                {
                    var replacement = Encoding.UTF8.GetBytes(replace(ReadEntry(source, entryName)));
                    targetStream.Write(replacement, 0, replacement.Length);
                }
                else
                {
                    using var sourceStream = sourceEntry.Open();
                    sourceStream.CopyTo(targetStream);
                }
            }
        }
        return output.ToArray();
    }

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find project root.");
    }
}
