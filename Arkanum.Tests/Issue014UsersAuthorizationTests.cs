using System.Security.Claims;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Pages.Users;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class Issue014UsersAuthorizationTests
{
    [Fact]
    public void LegacyUsersPageGetHandlerReturnsActionResultForAuthorizationFailures()
    {
        var method = typeof(IndexModel).GetMethod(nameof(IndexModel.OnGetAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IActionResult>), method!.ReturnType);
    }

    [Fact]
    public async Task AnonymousUsersCannotCreateLocalUsersThroughLegacyUsersPage()
    {
        await using var db = CreateDb(nameof(AnonymousUsersCannotCreateLocalUsersThroughLegacyUsersPage));
        var model = new IndexModel(db, new AdminUserService(new LocalPasswordHasher()))
        {
            PageContext = PageContextFor(null),
            Username = "anon-static-review",
            Password = "CorrectHorse!2026",
            SelectedRoles = [ArkanumRole.Administrator, ArkanumRole.User]
        };

        var result = await model.OnPostCreateLocalAsync();

        Assert.IsType<ChallengeResult>(result);
        Assert.False(await db.Users.AnyAsync(u => u.Username == "anon-static-review"));
    }

    [Fact]
    public async Task NonAdminUsersCannotCreateLocalUsersThroughLegacyUsersPage()
    {
        await using var db = CreateDb(nameof(NonAdminUsersCannotCreateLocalUsersThroughLegacyUsersPage));
        var model = new IndexModel(db, new AdminUserService(new LocalPasswordHasher()))
        {
            PageContext = PageContextFor("standard-user", ArkanumRole.User),
            Username = "non-admin-created",
            Password = "CorrectHorse!2026",
            SelectedRoles = [ArkanumRole.Administrator, ArkanumRole.User]
        };

        var result = await model.OnPostCreateLocalAsync();

        Assert.IsType<ForbidResult>(result);
        Assert.False(await db.Users.AnyAsync(u => u.Username == "non-admin-created"));
    }

    [Fact]
    public async Task AdministratorCanLoadAndCreateLocalUsersThroughLegacyUsersPage()
    {
        await using var db = CreateDb(nameof(AdministratorCanLoadAndCreateLocalUsersThroughLegacyUsersPage));
        db.Users.Add(new AppUserEntity
        {
            UserKey = "admin-key",
            Username = "admin",
            DisplayName = "Admin",
            Source = UserSource.Local,
            DirectoryPresent = true,
            AppAccessEnabled = true,
            PasswordHash = "hash",
            RolesCsv = "Administrator,User"
        });
        await db.SaveChangesAsync();

        var model = new IndexModel(db, new AdminUserService(new LocalPasswordHasher()))
        {
            PageContext = PageContextFor("admin-key", ArkanumRole.Administrator, ArkanumRole.User),
            Username = "admin-created",
            Password = "CorrectHorse!2026",
            SelectedRoles = [ArkanumRole.User]
        };

        var postResult = await model.OnPostCreateLocalAsync();

        Assert.IsType<RedirectToPageResult>(postResult);
        var created = await db.Users.SingleAsync(u => u.Username == "admin-created");
        Assert.True(created.AppAccessEnabled);
        Assert.Equal("admin-key", (await db.AuditEvents.SingleAsync(e => e.Target == "admin-created")).ActorUserKey);
    }

    [Fact]
    public async Task AdminUserServiceRejectsAnonymousCreateActorsBeforeAuditing()
    {
        await using var db = CreateDb(nameof(AdminUserServiceRejectsAnonymousCreateActorsBeforeAuditing));
        var service = new AdminUserService(new LocalPasswordHasher());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateLocalUserAsync(db, "bad-actor-created", "CorrectHorse!2026", [ArkanumRole.User], "anonymous", DateTimeOffset.UtcNow));

        Assert.False(await db.Users.AnyAsync(u => u.Username == "bad-actor-created"));
        Assert.Empty(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public void LegacyUsersPageModelCarriesAdministratorAuthorizeAttribute()
    {
        var attribute = typeof(IndexModel).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(nameof(ArkanumRole.Administrator), attribute!.Roles);
    }

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase($"issue014-{name}-{Guid.NewGuid():n}")
            .Options;
        return new ArkanumDbContext(options);
    }

    private static PageContext PageContextFor(string? userKey, params ArkanumRole[] roles)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrWhiteSpace(userKey))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userKey));
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role.ToString())));
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
}
