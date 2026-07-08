using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Pages.Vaults;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Arkanum.Tests;

public class InlineRevealAndAnchorTests
{
    [Fact]
    public void VaultMarkupRevealsSecretsInlineWithMaskedPlaceholderAndAnchoredForms()
    {
        var root = ProjectRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Details.cshtml"));
        var siteJs = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "wwwroot", "js", "site.js"));

        Assert.DoesNotContain("secret-reveal-panel", markup);
        Assert.Contains("id=\"vault-item-@item.Id\"", markup);
        Assert.Contains("password-mask", markup);
        Assert.Contains("******", markup);
        Assert.Contains("Model.RevealedItemId == item.Id", markup);
        Assert.Contains("data-preserve-scroll", markup);
        Assert.Contains("data-copy-target=\"revealedSecret-@item.Id\"", markup);
        Assert.Contains("copyButton.dataset.copyTarget", siteJs);
    }

    [Fact]
    public void VaultMarkupPreservesScrollAndKeepsCredentialRowsUncluttered()
    {
        var root = ProjectRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Details.cshtml"));
        var siteJs = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "wwwroot", "js", "site.js"));

        Assert.DoesNotContain("This reveal/copy action has been audited", markup);
        Assert.DoesNotContain("Revealed here", markup);
        Assert.Contains("data-preserve-scroll", markup);
        Assert.Contains("data-auto-submit-on-change", markup);
        Assert.Contains("class=\"reset-date-input\"", markup);
        Assert.DoesNotContain("<button class=\"secondary-action small\" type=\"submit\">Save</button>", markup);
        Assert.Contains("sessionStorage.setItem(vaultScrollKey", siteJs);
        Assert.Contains("data-auto-submit-on-change", siteJs);
    }

    [Fact]
    public void VaultMarkupGroupsItemsAndUsesTypeSpecificLabels()
    {
        var root = ProjectRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Details.cshtml"));
        var css = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "wwwroot", "css", "site.css"));

        Assert.Contains("Credentials", markup);
        Assert.Contains("Secure Notes", markup);
        Assert.Contains("folder.Items.Where(item => item.Type == VaultItemType.LoginCredential).OrderBy(item => item.Title)", markup);
        Assert.Contains("folder.Items.Where(item => item.Type == VaultItemType.SecureNote).OrderBy(item => item.Title)", markup);
        Assert.Contains("<span class=\"muted\">Username</span>", markup);
        Assert.Contains("<span class=\"muted\">@(item.Type == VaultItemType.SecureNote ? \"SecureNote\" : \"Password\")</span>", markup);
        Assert.Contains("vault-item-row", markup);
        Assert.Contains("vault-item-actions", markup);
        Assert.Contains("reset-notification-cell", markup);
        Assert.Contains("grid-template-columns: minmax(180px, 1.5fr) minmax(150px, 1fr) minmax(160px, 1fr) auto", css);
        Assert.Contains(".reset-notification-cell { justify-self: end", css);
    }

    [Fact]
    public void VaultFoldersDoNotExposeEntriesUntilUserExpandsThem()
    {
        var root = ProjectRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Details.cshtml"));

        Assert.Contains("<details class=\"tree-folder\">", markup);
        Assert.Contains("<details class=\"tree-subfolder\">", markup);
        Assert.DoesNotContain("<details class=\"tree-folder\" open>", markup);
        Assert.DoesNotContain("<details class=\"tree-subfolder\" open>", markup);
    }

    [Fact]
    public async Task RevealPostRedirectsBackToTheCredentialRowAnchor()
    {
        await using var db = CreateDb(nameof(RevealPostRedirectsBackToTheCredentialRowAnchor));
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var vault = new VaultEntity { Name = "Ops", Description = "Ops", CreatedByUserKey = "user", CreatedAt = now };
        var folder = new VaultFolderEntity { VaultId = vault.Id, Name = "General", CreatedAt = now };
        var crypto = new SecretEncryptionService(new StaticMasterKeyProvider("inline-anchor-key"));
        var encrypted = crypto.Encrypt("Secret!2026");
        var item = new VaultItemEntity
        {
            VaultId = vault.Id,
            FolderId = folder.Id,
            Type = VaultItemType.LoginCredential,
            Title = "Firewall admin",
            Username = "fwadmin",
            EncryptedSecret = encrypted.Ciphertext,
            EncryptionKeyId = encrypted.KeyId,
            EncryptionAlgorithm = encrypted.Algorithm,
            EncryptedAt = now
        };
        db.Vaults.Add(vault);
        db.VaultFolders.Add(folder);
        db.VaultMembers.Add(new VaultMemberEntity { VaultId = vault.Id, UserKey = "user", AddedByUserKey = "user", AddedAt = now });
        db.VaultItems.Add(item);
        await db.SaveChangesAsync();

        var pageContext = PageContextFor("user", ArkanumRole.User);
        var model = new DetailsModel(new VaultRepository(), new VaultItemService(crypto), new VaultExportService(crypto), db)
        {
            PageContext = pageContext,
            TempData = new TempDataDictionary(pageContext.HttpContext, new DictionaryTempDataProvider()),
            ItemId = item.Id,
            Title = item.Title
        };

        var result = await model.OnPostRevealAsync(vault.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.Fragment);
        Assert.Equal(item.Id, redirect.RouteValues!["revealedItemId"]);
        Assert.False(redirect.RouteValues!.ContainsKey("revealedSecret"));
        Assert.Equal("Secret!2026", model.TempData["RevealedSecret"]);
    }

    [Fact]
    public void SettingsPostHandlersRedirectToTheirTabAnchors()
    {
        var root = ProjectRoot();
        var settingsModel = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml.cs"));
        var settingsMarkup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));

        Assert.Contains("RedirectToSettingsTab(\"notifications\")", settingsModel);
        Assert.Contains("RedirectToSettingsTab(\"database\")", settingsModel);
        Assert.Contains("RedirectToSettingsTab(\"users\")", settingsModel);
        Assert.Contains("RedirectToSettingsTab(\"logs\")", settingsModel);
        Assert.Contains("data-preserve-settings-tab", settingsMarkup);
        Assert.Contains("data-preserve-scroll", settingsMarkup);
    }

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ArkanumDbContext(options);
    }

    private static PageContext PageContextFor(string userKey, params ArkanumRole[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userKey) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role.ToString())));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        return new PageContext { HttpContext = http };
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object> _data = [];
        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _data.Clear();
            foreach (var value in values) _data[value.Key] = value.Value;
        }
    }

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find project root.");
    }
}
