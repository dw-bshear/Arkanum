using System.Security.Claims;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class VaultDescriptionRequirementsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  \t")]
    public async Task Blank_description_is_rejected_before_any_records_are_created(string description)
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<ArgumentException>(() => new VaultRepository().CreateVaultAsync(db, "Operations", description, "owner", DateTimeOffset.UtcNow));
        Assert.Empty(await db.Vaults.ToListAsync());
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Creation_page_returns_field_validation_instead_of_throwing()
    {
        await using var db = CreateDb();
        var model = new Arkanum.Web.Pages.Vaults.IndexModel(new VaultRepository(), db)
        {
            Name = "Operations", Description = "  ",
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = Principal() } }
        };
        Assert.IsType<PageResult>(await model.OnPostCreateAsync());
        Assert.Contains("Description", model.ModelState.Keys);
        Assert.Empty(await db.Vaults.ToListAsync());
    }

    [Fact]
    public async Task Automatically_created_description_uses_display_name_and_requested_copy()
    {
        await using var db = CreateDb();
        db.Users.Add(new AppUserEntity { UserKey = "owner", Username = "alex", DisplayName = "Alex Example" });
        await db.SaveChangesAsync();
        var vault = await new VaultRepository().EnsurePersonalVaultAsync(db, "owner", "alex", DateTimeOffset.UtcNow);
        Assert.Equal("Alex Example's personal vault. Invite other users if you want to share your entries.", vault.Description);
    }

    [Theory]
    [InlineData("Your personal credential vault. Invite other users when you want to share access.", true)]
    [InlineData("", true)]
    [InlineData("My customized description", false)]
    public async Task Existing_personal_vault_defaults_are_updated_but_custom_copy_is_preserved(string oldDescription, bool shouldUpdate)
    {
        await using var db = CreateDb();
        var repo = new VaultRepository();
        var vault = new VaultEntity { Name = "alex's Vault", Description = oldDescription, CreatedByUserKey = "owner" };
        db.Vaults.Add(vault);
        db.VaultMembers.Add(new VaultMemberEntity { VaultId = vault.Id, UserKey = "owner", CanInvite = true });
        await db.SaveChangesAsync();
        var actual = await repo.EnsurePersonalVaultAsync(db, "owner", "alex", DateTimeOffset.UtcNow);
        Assert.Equal(vault.Id, actual.Id);
        Assert.Equal(shouldUpdate ? "alex's personal vault. Invite other users if you want to share your entries." : oldDescription, actual.Description);
        Assert.Single(await db.Vaults.ToListAsync());
    }

    [Fact]
    public void Creation_form_marks_description_required_and_preserves_failed_form_visibility()
    {
        var root = ProjectRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Arkanum.Web/Pages/Vaults/Index.cshtml"));
        Assert.Contains("asp-for=\"Description\" required", markup);
        Assert.Contains("asp-validation-for=\"Description\"", markup);
        Assert.Contains("open=\"@(!ViewData.ModelState.IsValid)\"", markup);
    }

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Name, "alex"),
        new Claim(ClaimTypes.Role, "Administrator")
    }, "Test"));

    private static ArkanumDbContext CreateDb() => new(new DbContextOptionsBuilder<ArkanumDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Project root missing");
    }
}
