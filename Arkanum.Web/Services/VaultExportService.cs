using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed record VaultExportFile(string FileName, string ContentType, byte[] Content);

public sealed class VaultExportService(SecretEncryptionService encryptionService)
{
    private const string ExportAlgorithm = "AES-256-GCM/PBKDF2-SHA256";
    private const int ExportEncryptionVersion = 1;
    private const int ExportKdfIterations = 200_000;
    private const string EncryptedCsvEntryName = "credentials.csv.aesgcm";

    public async Task<VaultExportFile> ExportVaultCsvZipAsync(ArkanumDbContext db, Guid vaultId, string actorUserKey, string passphrase, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var vault = await db.Vaults.AsNoTracking().SingleAsync(v => v.Id == vaultId, cancellationToken);
        var rows = await LoadRowsAsync(db, [vaultId], cancellationToken);
        var fileName = SafeFileName($"Arkanum-{vault.Name}-VaultExport-{now:yyyyMMddHHmmss}.zip");
        var content = BuildZip("vault", actorUserKey, now, rows, passphrase);
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.SecretReveal,
            ActorUserKey = actorUserKey,
            Target = vault.Name,
            OccurredAt = now,
            Message = $"Vault export generated for '{vault.Name}'. Included secret values: yes. Export file: {fileName}. Secret values were not logged."
        });
        await db.SaveChangesAsync(cancellationToken);
        return new VaultExportFile(fileName, "application/zip", content);
    }

    public async Task<VaultExportFile> ExportAllVaultsCsvZipAsync(ArkanumDbContext db, string actorUserKey, string passphrase, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var vaultIds = await db.Vaults.AsNoTracking().Select(v => v.Id).ToArrayAsync(cancellationToken);
        var rows = await LoadRowsAsync(db, vaultIds, cancellationToken);
        var fileName = $"Arkanum-AllVaultsExport-{now:yyyyMMddHHmmss}.zip";
        var content = BuildZip("all-vaults", actorUserKey, now, rows, passphrase);
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.SecretReveal,
            ActorUserKey = actorUserKey,
            Target = "All vaults",
            OccurredAt = now,
            Message = $"All-vault export generated. Included secret values: yes. Export file: {fileName}. Secret values were not logged."
        });
        await db.SaveChangesAsync(cancellationToken);
        return new VaultExportFile(fileName, "application/zip", content);
    }

    public static string ValidateAndDecryptExportCsvZip(byte[] zipContent, string passphrase)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(zipContent), ZipArchiveMode.Read);
            var manifestText = ReadEntry(zip, "manifest.json");
            using var manifestJson = JsonDocument.Parse(manifestText);
            var manifest = manifestJson.RootElement;
            var encryptedEntry = manifest.GetProperty("encryptedEntry").GetString() ?? throw new InvalidOperationException("Export manifest is missing encrypted entry metadata.");
            var algorithm = manifest.GetProperty("encryption").GetString();
            var version = manifest.GetProperty("encryptionVersion").GetInt32();
            if (!string.Equals(algorithm, ExportAlgorithm, StringComparison.Ordinal) || version != ExportEncryptionVersion)
            {
                throw new InvalidOperationException("Export encryption algorithm or version is not supported.");
            }

            var payloadText = ReadEntry(zip, encryptedEntry);
            var associatedData = BuildAssociatedData(
                manifest.GetProperty("product").GetString() ?? string.Empty,
                manifest.GetProperty("exportedAtUtc").GetString() ?? string.Empty,
                manifest.GetProperty("exportedBy").GetString() ?? string.Empty,
                manifest.GetProperty("scope").GetString() ?? string.Empty,
                manifest.GetProperty("itemCount").GetInt32(),
                encryptedEntry,
                algorithm!,
                version);
            return DecryptForExport(payloadText, passphrase, associatedData);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or InvalidDataException)
        {
            throw new InvalidOperationException("Export package integrity validation failed. The payload, manifest, or passphrase may be invalid.", ex);
        }
    }

    private async Task<List<ExportRow>> LoadRowsAsync(ArkanumDbContext db, IReadOnlyCollection<Guid> vaultIds, CancellationToken cancellationToken)
    {
        var rows = await db.VaultItems.AsNoTracking()
            .Where(i => vaultIds.Contains(i.VaultId))
            .Join(db.Vaults.AsNoTracking(), i => i.VaultId, v => v.Id, (i, v) => new { Item = i, Vault = v })
            .GroupJoin(db.VaultFolders.AsNoTracking(), x => x.Item.FolderId, f => f.Id, (x, folders) => new { x.Item, x.Vault, Folder = folders.FirstOrDefault() })
            .OrderBy(x => x.Vault.Name).ThenBy(x => x.Folder == null ? string.Empty : x.Folder.Name).ThenBy(x => x.Item.Title)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new ExportRow(
            x.Vault.Name,
            x.Folder?.Name ?? string.Empty,
            x.Item.Type.ToString(),
            x.Item.Title,
            x.Item.Username ?? string.Empty,
            encryptionService.Decrypt(new EncryptedSecretPayload(x.Item.EncryptedSecret, x.Item.EncryptionKeyId, x.Item.EncryptionAlgorithm)),
            x.Item.ResetNotificationDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            x.Item.EncryptedAt.ToString("O"))).ToList();
    }

    private static byte[] BuildZip(string scope, string actorUserKey, DateTimeOffset now, IReadOnlyList<ExportRow> rows, string passphrase)
    {
        using var stream = new MemoryStream();
        var exportedAtUtc = now.UtcDateTime.ToString("O");
        var associatedData = BuildAssociatedData("Arkanum", exportedAtUtc, actorUserKey, scope, rows.Count, EncryptedCsvEntryName, ExportAlgorithm, ExportEncryptionVersion);
        var encryptedCsv = EncryptForExport(ToCsv(rows), passphrase, associatedData);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "manifest.json", JsonSerializer.Serialize(new
            {
                product = "Arkanum",
                exportedAtUtc,
                exportedBy = actorUserKey,
                scope,
                includesSecretValues = true,
                format = "csv.aesgcm",
                itemCount = rows.Count,
                encryption = ExportAlgorithm,
                encryptionVersion = ExportEncryptionVersion,
                encryptedEntry = EncryptedCsvEntryName,
                integrity = new
                {
                    algorithm = ExportAlgorithm,
                    tagBytes = 16,
                    authenticatedMetadata = new[] { "product", "exportedAtUtc", "exportedBy", "scope", "itemCount", "encryptedEntry", "encryption", "encryptionVersion" }
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
            AddEntry(zip, EncryptedCsvEntryName, encryptedCsv);
        }
        return stream.ToArray();
    }

    private static string EncryptForExport(string plaintext, string passphrase, string associatedData)
    {
        if (string.IsNullOrWhiteSpace(passphrase)) throw new InvalidOperationException("Export passphrase is required.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, ExportKdfIterations, HashAlgorithmName.SHA256, 32);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag, Encoding.UTF8.GetBytes(associatedData));
        CryptographicOperations.ZeroMemory(key);
        return JsonSerializer.Serialize(new
        {
            algorithm = ExportAlgorithm,
            version = ExportEncryptionVersion,
            kdf = "PBKDF2-SHA256",
            iterations = ExportKdfIterations,
            salt = Convert.ToBase64String(salt),
            nonce = Convert.ToBase64String(nonce),
            tag = Convert.ToBase64String(tag),
            ciphertext = Convert.ToBase64String(cipherBytes)
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string DecryptForExport(string payload, string passphrase, string associatedData)
    {
        if (string.IsNullOrWhiteSpace(passphrase)) throw new InvalidOperationException("Export passphrase is required.");
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        if (!string.Equals(root.GetProperty("algorithm").GetString(), ExportAlgorithm, StringComparison.Ordinal) || root.GetProperty("version").GetInt32() != ExportEncryptionVersion)
        {
            throw new InvalidOperationException("Export encryption payload algorithm or version is not supported.");
        }

        var salt = Convert.FromBase64String(root.GetProperty("salt").GetString() ?? string.Empty);
        var nonce = Convert.FromBase64String(root.GetProperty("nonce").GetString() ?? string.Empty);
        var tag = Convert.FromBase64String(root.GetProperty("tag").GetString() ?? string.Empty);
        var cipherBytes = Convert.FromBase64String(root.GetProperty("ciphertext").GetString() ?? string.Empty);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, root.GetProperty("iterations").GetInt32(), HashAlgorithmName.SHA256, 32);
        var plainBytes = new byte[cipherBytes.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes, Encoding.UTF8.GetBytes(associatedData));
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Export package integrity validation failed. The payload, manifest, or passphrase may be invalid.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private static string BuildAssociatedData(string product, string exportedAtUtc, string exportedBy, string scope, int itemCount, string encryptedEntry, string encryption, int encryptionVersion)
    {
        return string.Join("\n", product, exportedAtUtc, exportedBy, scope, itemCount.ToString(System.Globalization.CultureInfo.InvariantCulture), encryptedEntry, encryption, encryptionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string ToCsv(IReadOnlyList<ExportRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("VaultName,FolderPath,ItemType,Title,Username,SecretValue,ResetNotificationDate,EncryptedAt");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', Escape(row.VaultName), Escape(row.FolderPath), Escape(row.ItemType), Escape(row.Title), Escape(row.Username), Escape(row.SecretValue), Escape(row.ResetNotificationDate), Escape(row.EncryptedAt)));
        }
        return builder.ToString();
    }

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string ReadEntry(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"Export package is missing {entryName}.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string SafeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
        return value.Replace(' ', '-');
    }

    private sealed record ExportRow(string VaultName, string FolderPath, string ItemType, string Title, string Username, string SecretValue, string ResetNotificationDate, string EncryptedAt);
}
