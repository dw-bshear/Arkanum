using System.Security.Claims;
using System.Text.Json;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Pages.Vaults;
using Arkanum.Web.Services;
using Arkanum.Web.Setup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class CredentialHistoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-21T12:00:00Z");
    private static readonly ArkanumRole[] Roles = [ArkanumRole.User];
    private static SecretEncryptionService Crypto() => new(new StaticMasterKeyProvider("history-tests-only-master-key"));
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public async Task Saved_versions_include_current_newest_first_and_retain_only_five_encrypted_snapshots()
    {
        await using var fixture = await Fixture.Create();
        var db = fixture.Db;
        var service = new VaultItemService(Crypto());
        var item = await service.CreateLoginCredentialAsync(db, fixture.Vault.Id, fixture.Folder.Id, "original title", "original user", "secret-0", "owner", Now);
        for (var n = 1; n <= 7; n++)
            await service.EditLoginCredentialAsync(db, fixture.Vault.Id, item.Id, fixture.Folder.Id, $"title-{n}", $"user-{n}", $"secret-{n}", "owner", Roles, Now.AddMinutes(n));
        var versions = await service.ListCredentialHistoryAsync(db, fixture.Vault.Id, item.Id, "owner", Roles);
        Assert.Equal(new long[] { 8, 7, 6, 5, 4 }, versions.Select(v => v.Revision));
        var rows = await db.CredentialVersions.AsNoTracking().ToListAsync();
        Assert.Equal(5, rows.Count);
        foreach (var row in rows)
        {
            Assert.DoesNotContain("secret-", row.EncryptedPayload);
            Assert.DoesNotContain("title-", row.EncryptedPayload);
            Assert.DoesNotContain("user-", row.EncryptedPayload);
        }
        var revealed = await service.RevealCredentialVersionAsync(db, fixture.Vault.Id, item.Id, versions[1].Id, "owner", Roles, Now);
        Assert.Equal("secret-6", revealed.Password);
        Assert.Equal("title-6", revealed.Title);
        Assert.Equal("user-6", revealed.Username);
        Assert.Equal("secret-7", await service.RevealSecretAsync(db, fixture.Vault.Id, item.Id, "owner", SecretAccessPurpose.Reveal, Now));
        Assert.DoesNotContain("secret-", JsonSerializer.Serialize(await db.AuditEvents.ToListAsync()));
        Assert.DoesNotContain("user-", JsonSerializer.Serialize(await db.AuditEvents.ToListAsync()));
    }

    [Fact]
    public async Task Legacy_first_edit_snapshots_original_and_blank_password_preserves_current()
    {
        await using var fixture = await Fixture.Create();
        var db = fixture.Db;
        var crypto = Crypto();
        var encrypted = crypto.Encrypt("legacy-password");
        var item = new VaultItemEntity { VaultId = fixture.Vault.Id, FolderId = fixture.Folder.Id, Type = VaultItemType.LoginCredential, Title = "legacy title", Username = "legacy user", EncryptedSecret = encrypted.Ciphertext, EncryptionKeyId = encrypted.KeyId, EncryptionAlgorithm = encrypted.Algorithm, EncryptedAt = Now.AddDays(-10) };
        db.VaultItems.Add(item);
        await db.SaveChangesAsync();
        var service = new VaultItemService(crypto);
        await service.EditLoginCredentialAsync(db, fixture.Vault.Id, item.Id, fixture.Folder.Id, "edited", "updated", null, "owner", Roles, Now);
        var versions = await service.ListCredentialHistoryAsync(db, fixture.Vault.Id, item.Id, "owner", Roles);
        Assert.Equal(new long[] { 2, 1 }, versions.Select(v => v.Revision));
        var original = await service.RevealCredentialVersionAsync(db, fixture.Vault.Id, item.Id, versions[1].Id, "owner", Roles, Now);
        Assert.Equal("legacy title", original.Title);
        Assert.Equal("legacy user", original.Username);
        Assert.Equal("legacy-password", original.Password);
        Assert.Equal(Now.AddDays(-10), versions[1].SavedAt);
        Assert.Equal("legacy-password", await service.RevealSecretAsync(db, fixture.Vault.Id, item.Id, "owner", SecretAccessPurpose.Reveal, Now));
    }

    [Theory]
    [InlineData("outsider", ArkanumRole.User)]
    [InlineData("owner", ArkanumRole.Auditor)]
    public async Task Unauthorized_roles_or_nonmembers_cannot_edit_list_or_reveal(string actor, ArkanumRole role)
    {
        await using var fixture = await Fixture.Create();
        var service = new VaultItemService(Crypto());
        var item = await service.CreateLoginCredentialAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "item", "user", "secret", "owner", Now);
        var version = await fixture.Db.CredentialVersions.SingleAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.EditLoginCredentialAsync(fixture.Db, fixture.Vault.Id, item.Id, fixture.Folder.Id, "changed", "changed", "changed", actor, [role], Now));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListCredentialHistoryAsync(fixture.Db, fixture.Vault.Id, item.Id, actor, [role]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RevealCredentialVersionAsync(fixture.Db, fixture.Vault.Id, item.Id, version.Id, actor, [role], Now));
        Assert.Single(await fixture.Db.CredentialVersions.ToListAsync());
    }

    [Fact]
    public async Task Cross_vault_item_folder_and_cross_item_version_are_denied_before_any_write()
    {
        await using var fixture = await Fixture.Create();
        var service = new VaultItemService(Crypto());
        var item = await service.CreateLoginCredentialAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "item", "user", "secret", "owner", Now);
        var other = await service.CreateLoginCredentialAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "other", "user", "other secret", "owner", Now);
        var version = await fixture.Db.CredentialVersions.SingleAsync(v => v.ItemId == other.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.EditLoginCredentialAsync(fixture.Db, fixture.Vault.Id, Guid.NewGuid(), fixture.Folder.Id, "changed", "", "x", "owner", Roles, Now));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.EditLoginCredentialAsync(fixture.Db, fixture.Vault.Id, item.Id, Guid.NewGuid(), "changed", "", "x", "owner", Roles, Now));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RevealCredentialVersionAsync(fixture.Db, fixture.Vault.Id, item.Id, version.Id, "owner", Roles, Now));
        Assert.Equal(2, await fixture.Db.CredentialVersions.CountAsync());
    }

    [Fact]
    public async Task Existing_sqlite_upgrade_is_additive_idempotent_and_preserves_original_ciphertext()
    {
        await using var fixture = await Fixture.Create();
        var db = fixture.Db;
        var service = new VaultItemService(Crypto());
        var item = await service.CreateLoginCredentialAsync(db, fixture.Vault.Id, fixture.Folder.Id, "item", "user", "secret", "owner", Now);
        var cipher = item.EncryptedSecret;
        // Reproduce an existing pre-history database, not EnsureCreated's fresh schema.
        await db.Database.ExecuteSqlRawAsync("DROP TABLE CredentialVersions");
        db.ChangeTracker.Clear();
        await db.EnsureCredentialHistorySchemaAsync();
        await db.EnsureCredentialHistorySchemaAsync();
        Assert.Empty(await db.CredentialVersions.ToListAsync());
        Assert.Equal(cipher, (await db.VaultItems.SingleAsync()).EncryptedSecret);
        await service.EditLoginCredentialAsync(db, fixture.Vault.Id, item.Id, fixture.Folder.Id, "new", "new", "new secret", "owner", Roles, Now);
        Assert.Equal(2, await db.CredentialVersions.CountAsync());
    }

    [Fact]
    public async Task Page_history_requires_explicit_post_and_reveals_only_selected_version_with_no_cache()
    {
        await using var fixture = await Fixture.Create();
        var crypto = Crypto();
        var service = new VaultItemService(crypto);
        var item = await service.CreateLoginCredentialAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "item", "user", "private-history-value", "owner", Now);
        var page = Page(fixture.Db, "owner", ArkanumRole.User);
        Assert.IsType<PageResult>(await page.OnGetAsync(fixture.Vault.Id));
        Assert.Empty(page.CredentialHistory);
        Assert.Null(page.RevealedHistory);
        page.ItemId = item.Id;
        Assert.IsType<PageResult>(await page.OnPostHistoryAsync(fixture.Vault.Id));
        Assert.Single(page.CredentialHistory);
        Assert.Null(page.RevealedHistory);
        page.VersionId = page.CredentialHistory[0].Id;
        Assert.IsType<PageResult>(await page.OnPostRevealHistoryAsync(fixture.Vault.Id));
        Assert.Equal("private-history-value", page.RevealedHistory!.Password);
        Assert.Contains("no-store", page.Response.Headers.CacheControl.ToString());
        Assert.DoesNotContain("private-history-value", JsonSerializer.Serialize(await fixture.Db.AuditEvents.ToListAsync()));
    }

    [Theory]
    [InlineData("owner", ArkanumRole.Auditor)]
    [InlineData("outsider", ArkanumRole.User)]
    public async Task Page_edit_list_and_reveal_handlers_forbid_unauthorized_users(string actor, ArkanumRole role)
    {
        await using var fixture = await Fixture.Create();
        var page = Page(fixture.Db, actor, role);
        Assert.IsType<ForbidResult>(await page.OnPostEditCredentialAsync(fixture.Vault.Id));
        Assert.IsType<ForbidResult>(await page.OnPostHistoryAsync(fixture.Vault.Id));
        Assert.IsType<ForbidResult>(await page.OnPostRevealHistoryAsync(fixture.Vault.Id));
    }

    [Fact]
    public async Task Page_edit_saves_metadata_and_password_without_secret_in_redirect()
    {
        await using var fixture = await Fixture.Create();
        var service = new VaultItemService(Crypto());
        var item = await service.CreateLoginCredentialAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "item", "user", "old", "owner", Now);
        var page = Page(fixture.Db, "owner", ArkanumRole.User);
        page.ItemId = item.Id; page.FolderId = fixture.Folder.Id; page.Title = "new title"; page.Username = "new user"; page.SecretValue = "new-password";
        var result = Assert.IsType<RedirectToPageResult>(await page.OnPostEditCredentialAsync(fixture.Vault.Id));
        Assert.DoesNotContain("new-password", JsonSerializer.Serialize(result.RouteValues));
        Assert.Equal("new title", item.Title);
        Assert.Equal("new user", item.Username);
        Assert.Null(page.SecretValue);
        Assert.Equal("new-password", await service.RevealSecretAsync(fixture.Db, fixture.Vault.Id, item.Id, "owner", SecretAccessPurpose.Reveal, Now));
    }

    [Fact]
    public async Task Invalid_edit_model_does_not_save_or_echo_posted_password()
    {
        await using var fixture = await Fixture.Create();
        var service = new VaultItemService(Crypto());
        var item = await service.CreateLoginCredentialAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "item", "user", "old", "owner", Now);
        var page = Page(fixture.Db, "owner", ArkanumRole.User);
        page.ItemId = item.Id; page.FolderId = fixture.Folder.Id; page.Title = "changed"; page.SecretValue = "do-not-echo";
        page.ModelState.AddModelError("FolderId", "Invalid folder.");
        Assert.IsType<PageResult>(await page.OnPostEditCredentialAsync(fixture.Vault.Id));
        Assert.Single(await fixture.Db.CredentialVersions.ToListAsync());
        Assert.Equal("item", item.Title);
        Assert.Null(page.SecretValue);
    }

    [Fact]
    public async Task Stale_context_edit_rolls_back_current_history_and_audit_together()
    {
        await using var fixture = await Fixture.Create();
        var service = new VaultItemService(Crypto());
        var item = await service.CreateLoginCredentialAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "item", "user", "original", "owner", Now);
        await using var stale = new ArkanumDbContext(new DbContextOptionsBuilder<ArkanumDbContext>().UseSqlite(fixture.Db.Database.GetDbConnection()).Options);
        await stale.VaultItems.SingleAsync(i => i.Id == item.Id);
        await service.EditLoginCredentialAsync(fixture.Db, fixture.Vault.Id, item.Id, fixture.Folder.Id, "winner", "winner", "winning-secret", "owner", Roles, Now);
        var audits = await fixture.Db.AuditEvents.CountAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.EditLoginCredentialAsync(stale, fixture.Vault.Id, item.Id, fixture.Folder.Id, "stale", "stale", "losing-secret", "owner", Roles, Now));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal("winner", (await fixture.Db.VaultItems.SingleAsync()).Title);
        Assert.Equal(2, await fixture.Db.CredentialVersions.CountAsync());
        Assert.Equal(audits, await fixture.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Retired_vault_empty_roles_and_note_items_are_denied_by_history_service()
    {
        await using var fixture = await Fixture.Create();
        var service = new VaultItemService(Crypto());
        var item = await service.CreateLoginCredentialAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "item", "user", "secret", "owner", Now);
        var note = await service.CreateSecureNoteAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "note", "note secret", "owner", Now);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListCredentialHistoryAsync(fixture.Db, fixture.Vault.Id, item.Id, "owner", []));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.EditLoginCredentialAsync(fixture.Db, fixture.Vault.Id, note.Id, fixture.Folder.Id, "changed", "", "x", "owner", Roles, Now));
        fixture.Vault.IsRetired = true;
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListCredentialHistoryAsync(fixture.Db, fixture.Vault.Id, item.Id, "owner", Roles));
    }

    [Fact]
    public async Task Page_handlers_reject_cross_vault_items_and_versions_from_other_items()
    {
        await using var fixture = await Fixture.Create();
        var service = new VaultItemService(Crypto());
        var item = await service.CreateLoginCredentialAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "item", "user", "secret", "owner", Now);
        var other = await service.CreateLoginCredentialAsync(fixture.Db, fixture.Vault.Id, fixture.Folder.Id, "other", "user", "other secret", "owner", Now);
        var page = Page(fixture.Db, "owner", ArkanumRole.User);
        page.ItemId = Guid.NewGuid(); page.FolderId = fixture.Folder.Id; page.Title = "forged"; page.SecretValue = "forged";
        Assert.IsType<ForbidResult>(await page.OnPostEditCredentialAsync(fixture.Vault.Id));
        Assert.IsType<ForbidResult>(await page.OnPostHistoryAsync(fixture.Vault.Id));
        Assert.IsType<ForbidResult>(await page.OnPostRevealHistoryAsync(fixture.Vault.Id));
        page.ItemId = item.Id;
        page.VersionId = (await fixture.Db.CredentialVersions.SingleAsync(v => v.ItemId == other.Id)).Id;
        Assert.IsType<ForbidResult>(await page.OnPostRevealHistoryAsync(fixture.Vault.Id));
        Assert.Null(page.RevealedHistory);
    }

    private static DetailsModel Page(ArkanumDbContext db, string actor, ArkanumRole role)
    {
        var crypto = Crypto();
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, actor), new Claim(ClaimTypes.Role, role.ToString()) }, "test")) };
        return new DetailsModel(new VaultRepository(), new VaultItemService(crypto), new VaultExportService(crypto), db)
        {
            PageContext = new PageContext { HttpContext = context },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(context, new EmptyTempDataProvider())
        };
    }
    private sealed class EmptyTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class Fixture(SqliteConnection connection, ArkanumDbContext db, VaultEntity vault, VaultFolderEntity folder) : IAsyncDisposable
    {
        public ArkanumDbContext Db { get; } = db;
        public VaultEntity Vault { get; } = vault;
        public VaultFolderEntity Folder { get; } = folder;
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ArkanumDbContext(new DbContextOptionsBuilder<ArkanumDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var vault = new VaultEntity { Name = "test", Description = "test", CreatedByUserKey = "owner" };
            var folder = new VaultFolderEntity { VaultId = vault.Id, Name = "General" };
            db.Vaults.Add(vault);
            db.VaultFolders.Add(folder);
            db.VaultMembers.Add(new VaultMemberEntity { VaultId = vault.Id, UserKey = "owner" });
            await db.SaveChangesAsync();
            return new Fixture(connection, db, vault, folder);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    [Fact]
    public void Installer_schema_preflight_and_runtime_grants_include_encrypted_history()
    {
        var sql = File.ReadAllText(Path.Combine(Root(), "installers/sql/arkanum-schema.sql"));
        Assert.Contains("CREATE TABLE [CredentialVersions]", sql);
        var contract = SetupSql.ReadSchemaContract(sql);
        Assert.Contains(contract.Columns, c => c.Table == "CredentialVersions" && c.Name == "EncryptedPayload");
        Assert.Contains(contract.Indexes, i => i.Table == "CredentialVersions" && i.Unique && i.Columns.SequenceEqual(new[] { "ItemId", "Revision" }));
        Assert.Contains("ON OBJECT::[dbo].[CredentialVersions]", SetupSql.RuntimePermissions("runtime", contract));
    }

    [Fact]
    public void Details_has_explicit_edit_history_and_reveal_posts_without_prerendered_history_secrets()
    {
        var markup = File.ReadAllText(Path.Combine(Root(), "Arkanum.Web/Pages/Vaults/Details.cshtml"));
        Assert.Contains("asp-page-handler=\"EditCredential\"", markup);
        Assert.Contains("asp-page-handler=\"History\"", markup);
        Assert.Contains("asp-page-handler=\"RevealHistory\"", markup);
        Assert.Contains("including the current version", markup);
        Assert.DoesNotContain("value=\"@Model.SecretValue", markup);
        Assert.Contains("ResponseCache", File.ReadAllText(Path.Combine(Root(), "Arkanum.Web/Pages/Vaults/Details.cshtml.cs")));
    }
}
