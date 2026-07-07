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
        var csv = ToCsv(rows);
        var encryptedCsv = EncryptForExport(csv, passphrase);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "manifest.json", JsonSerializer.Serialize(new
            {
                product = "Arkanum",
                exportedAtUtc = now.UtcDateTime.ToString("O"),
                exportedBy = actorUserKey,
                scope,
                includesSecretValues = true,
                format = "csv.aes",
                itemCount = rows.Count,
                encryption = "AES-256-CBC/PBKDF2-SHA256",
                encryptedEntry = "credentials.csv.aes"
            }, new JsonSerializerOptions { WriteIndented = true }));
            AddEntry(zip, "credentials.csv.aes", encryptedCsv);
        }
        return stream.ToArray();
    }

    private static string EncryptForExport(string plaintext, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase)) throw new InvalidOperationException("Export passphrase is required.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        using var derive = new Rfc2898DeriveBytes(passphrase, salt, 200_000, HashAlgorithmName.SHA256);
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = derive.GetBytes(32);
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return JsonSerializer.Serialize(new
        {
            algorithm = "AES-256-CBC/PBKDF2-SHA256",
            iterations = 200000,
            salt = Convert.ToBase64String(salt),
            iv = Convert.ToBase64String(iv),
            ciphertext = Convert.ToBase64String(cipher)
        }, new JsonSerializerOptions { WriteIndented = true });
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

    private static string SafeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
        return value.Replace(' ', '-');
    }

    private sealed record ExportRow(string VaultName, string FolderPath, string ItemType, string Title, string Username, string SecretValue, string ResetNotificationDate, string EncryptedAt);
}
