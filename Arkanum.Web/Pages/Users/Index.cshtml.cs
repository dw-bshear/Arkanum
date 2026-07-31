using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Arkanum.Web.Pages.Users;

[Authorize(Roles = nameof(ArkanumRole.Administrator))]
public class IndexModel(ArkanumDbContext db, AdminUserService adminUserService) : PageModel
{
    public IReadOnlyList<AppUserEntity> Users { get; private set; } = [];
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public List<ArkanumRole> SelectedRoles { get; set; } = [ArkanumRole.User];

    public async Task<IActionResult> OnGetAsync()
    {
        var authorizationFailure = RequireAdministrator();
        if (authorizationFailure is not null) return authorizationFailure;

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateLocalAsync()
    {
        var authorizationFailure = RequireAdministrator();
        if (authorizationFailure is not null) return authorizationFailure;

        if (string.IsNullOrWhiteSpace(Username)) ModelState.AddModelError(nameof(Username), "Username is required.");
        if (Password.Length < 12) ModelState.AddModelError(nameof(Password), "Password must be at least 12 characters long.");
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }
        await adminUserService.CreateLocalUserAsync(db, Username, Password, SelectedRoles, ActorKey(), DateTimeOffset.UtcNow);
        return RedirectToPage();
    }

    private IActionResult? RequireAdministrator()
    {
        if (User?.Identity?.IsAuthenticated != true) return Challenge();
        return User.IsInRole(nameof(ArkanumRole.Administrator)) ? null : Forbid();
    }

    private string ActorKey() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("Authenticated administrator principal is missing a stable user key.");

    private async Task LoadAsync()
    {
        Users = await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync();
    }
}
