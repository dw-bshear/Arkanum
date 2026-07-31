using System.Reflection;
using System.Security.Claims;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Pages.Vaults;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuditIndexModel = Arkanum.Web.Pages.Audit.IndexModel;
using SettingsIndexModel = Arkanum.Web.Pages.Settings.IndexModel;
using VaultIndexModel = Arkanum.Web.Pages.Vaults.IndexModel;

namespace Arkanum.Tests;

public sealed class Issue016RoleFallbackTests
{
    [Fact]
    public async Task VaultCreateWithAuthenticatedPrincipalMissingRoleClaimsFailsClosed()
    {
        await using var db = CreateDb(nameof(VaultCreateWithAuthenticatedPrincipalMissingRoleClaimsFailsClosed));
        var model = new VaultIndexModel(new VaultRepository(), db)
        {
            PageContext = PageContextFor("roleless-user"),
            Name = "Should not be created"
        };

        var result = await model.OnPostCreateAsync();

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await db.Vaults.ToListAsync());
    }

    [Fact]
    public void VaultIndexRoleHelperDoesNotConvertMissingOrMalformedClaimsToUser()
    {
        using var db = CreateDb(nameof(VaultIndexRoleHelperDoesNotConvertMissingOrMalformedClaimsToUser));
        var model = new VaultIndexModel(new VaultRepository(), db)
        {
            PageContext = PageContextFor("roleless-user", "not-a-real-role")
        };

        var roles = InvokeRoleSet(model, "CurrentRoles");

        Assert.Empty(roles);
    }

    [Fact]
    public void VaultDetailsRoleHelperDoesNotConvertMissingOrMalformedClaimsToUser()
    {
        using var db = CreateDb(nameof(VaultDetailsRoleHelperDoesNotConvertMissingOrMalformedClaimsToUser));
        var crypto = new SecretEncryptionService(new StaticMasterKeyProvider("issue016-master-key-material"));
        var model = new DetailsModel(new VaultRepository(), new VaultItemService(crypto), new VaultExportService(crypto), db)
        {
            PageContext = PageContextFor("roleless-user", "not-a-real-role")
        };

        var roles = InvokeRoleSet(model, "CurrentRoles");

        Assert.Empty(roles);
    }

    [Fact]
    public void AuditRoleHelperDoesNotConvertMissingOrMalformedClaimsToUser()
    {
        using var db = CreateDb(nameof(AuditRoleHelperDoesNotConvertMissingOrMalformedClaimsToUser));
        var model = new AuditIndexModel(db)
        {
            PageContext = PageContextFor("roleless-user", "not-a-real-role")
        };

        var roles = InvokeRoleSet(model, "CurrentRoles");

        Assert.Empty(roles);
    }

    [Fact]
    public async Task SettingsRoleLoaderDoesNotConvertMissingOrMalformedClaimsToUser()
    {
        await using var db = CreateDb(nameof(SettingsRoleLoaderDoesNotConvertMissingOrMalformedClaimsToUser));
        var crypto = new SecretEncryptionService(new StaticMasterKeyProvider("issue016-master-key-material"));
        var model = new SettingsIndexModel(
            db,
            new DashboardService(),
            new PortalSettingsService(),
            new AdminUserService(new LocalPasswordHasher()),
            new DirectoryLdapImportService(),
            new VaultExportService(crypto))
        {
            PageContext = PageContextFor("roleless-user", "not-a-real-role")
        };

        await InvokePrivateTask(model, "LoadRolesAsync");

        Assert.Empty(model.CurrentRoles);
    }

    [Fact]
    public void SourceDoesNotContainRequestTimeUserRoleFallbacks()
    {
        var root = ProjectRoot();
        foreach (var relativePath in new[]
        {
            Path.Combine("Arkanum.Web", "Pages", "Index.cshtml.cs"),
            Path.Combine("Arkanum.Web", "Pages", "Vaults", "Index.cshtml.cs"),
            Path.Combine("Arkanum.Web", "Pages", "Vaults", "Details.cshtml.cs"),
            Path.Combine("Arkanum.Web", "Pages", "Audit", "Index.cshtml.cs"),
            Path.Combine("Arkanum.Web", "Pages", "Settings", "Index.cshtml.cs")
        })
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotContain("roles.Count == 0 ? [ArkanumRole.User]", source);
            Assert.DoesNotContain("roles.Length == 0 ? [ArkanumRole.User]", source);
            Assert.DoesNotContain("?? \"anonymous\"", source);
        }
    }

    private static HashSet<ArkanumRole> InvokeRoleSet(object model, string methodName)
    {
        var method = model.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not locate {methodName} on {model.GetType().Name}.");
        return Assert.IsType<HashSet<ArkanumRole>>(method.Invoke(model, []));
    }

    private static async Task InvokePrivateTask(object model, string methodName)
    {
        var method = model.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not locate {methodName} on {model.GetType().Name}.");
        await Assert.IsAssignableFrom<Task>(method.Invoke(model, []));
    }

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase($"issue016-{name}-{Guid.NewGuid():n}")
            .Options;
        return new ArkanumDbContext(options);
    }

    private static PageContext PageContextFor(string? userKey, params string[] roleClaims)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrWhiteSpace(userKey))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userKey));
            claims.Add(new Claim(ClaimTypes.Name, userKey));
            claims.AddRange(roleClaims.Select(role => new Claim(ClaimTypes.Role, role)));
        }

        var identity = claims.Count == 0
            ? new ClaimsIdentity()
            : new ClaimsIdentity(claims, "TestAuth");
        return new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arkanum.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate Arkanum.sln.");
    }
}
