using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class Iteration006FolderTests
{
    [Fact]
    public async Task Vault_owner_can_create_additional_folder_and_details_show_it()
    {
        await using var db = CreateDb();
        var repository = new VaultRepository();
        var owner = new AppUserEntity { UserKey = "owner-key", Username = "owner", DisplayName = "owner", AppAccessEnabled = true, RolesCsv = nameof(ArkanumRole.User) };
        db.Users.Add(owner);
        var vault = await repository.EnsurePersonalVaultAsync(db, owner.UserKey, owner.Username, DateTimeOffset.Parse("2026-06-27T02:00:00Z"));

        await repository.CreateFolderAsync(db, vault.Id, " Network devices ", owner.UserKey, DateTimeOffset.Parse("2026-06-27T03:00:00Z"));
        var details = await repository.GetVaultDetailsAsync(db, vault.Id);

        Assert.NotNull(details);
        Assert.Contains(details!.Folders, f => f.Name == "General");
        Assert.Contains(details.Folders, f => f.Name == "Network devices");
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Target == vault.Name && e.Message.Contains("Network devices"));
    }

    [Fact]
    public async Task Create_folder_rejects_duplicate_names_in_same_vault()
    {
        await using var db = CreateDb();
        var repository = new VaultRepository();
        var owner = new AppUserEntity { UserKey = "owner-key", Username = "owner", DisplayName = "owner", AppAccessEnabled = true, RolesCsv = nameof(ArkanumRole.User) };
        db.Users.Add(owner);
        var vault = await repository.EnsurePersonalVaultAsync(db, owner.UserKey, owner.Username, DateTimeOffset.Parse("2026-06-27T02:00:00Z"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateFolderAsync(db, vault.Id, "general", owner.UserKey, DateTimeOffset.Parse("2026-06-27T03:00:00Z")));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task Adding_login_credentials_and_secure_notes_puts_items_in_selected_folder_and_reveals_plaintext()
    {
        await using var db = CreateDb();
        var repository = new VaultRepository();
        var itemService = new VaultItemService(new SecretEncryptionService(new StaticMasterKeyProvider("unit-test-master-key-material")));
        var owner = new AppUserEntity { UserKey = "owner-key", Username = "owner", DisplayName = "owner", AppAccessEnabled = true, RolesCsv = nameof(ArkanumRole.User) };
        db.Users.Add(owner);
        var vault = await repository.EnsurePersonalVaultAsync(db, owner.UserKey, owner.Username, DateTimeOffset.Parse("2026-06-27T02:00:00Z"));
        var folder = await repository.CreateFolderAsync(db, vault.Id, "Network devices", owner.UserKey, DateTimeOffset.Parse("2026-06-27T03:00:00Z"));

        var credential = await itemService.CreateLoginCredentialAsync(db, vault.Id, folder.Id, "Firewall admin", "admin@example.com", "P@ssw0rd!2345", owner.UserKey, DateTimeOffset.Parse("2026-06-27T04:00:00Z"));
        var note = await itemService.CreateSecureNoteAsync(db, vault.Id, folder.Id, "Recovery procedure", "Call the network lead before reset.", owner.UserKey, DateTimeOffset.Parse("2026-06-27T04:05:00Z"));
        var details = await repository.GetVaultDetailsAsync(db, vault.Id);

        var networkFolder = Assert.Single(details!.Folders, f => f.Name == "Network devices");
        Assert.Contains(networkFolder.Items, i => i.Id == credential.Id && i.Type == VaultItemType.LoginCredential && i.Username == "admin@example.com");
        Assert.Contains(networkFolder.Items, i => i.Id == note.Id && i.Type == VaultItemType.SecureNote);
        Assert.Equal("P@ssw0rd!2345", await itemService.RevealSecretAsync(db, credential.Id, owner.UserKey, SecretAccessPurpose.Reveal, DateTimeOffset.Parse("2026-06-27T04:10:00Z")));
        Assert.Equal("Call the network lead before reset.", await itemService.RevealSecretAsync(db, note.Id, owner.UserKey, SecretAccessPurpose.Reveal, DateTimeOffset.Parse("2026-06-27T04:11:00Z")));
    }

    [Fact]
    public void Vault_detail_renders_new_folder_disclosure()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Vaults", "Details.cshtml"));

        Assert.Contains("class=\"new-folder-disclosure\"", markup);
        Assert.Contains("<summary class=\"secondary-action small\">New folder</summary>", markup);
        Assert.Contains("asp-page-handler=\"CreateFolder\"", markup);
        Assert.Contains("asp-for=\"FolderName\"", markup);
    }

    private static ArkanumDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("n"))
            .Options;
        var db = new ArkanumDbContext(options);
        db.Database.EnsureCreated();
        return db;
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
