using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Arkanum.Web.Pages.Users;

public class IndexModel(ArkanumDbContext db, AdminUserService adminUserService) : PageModel
{
    public IReadOnlyList<AppUserEntity> Users { get; private set; } = [];
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public List<ArkanumRole> SelectedRoles { get; set; } = [ArkanumRole.User];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateLocalAsync()
    {
        if (string.IsNullOrWhiteSpace(Username)) ModelState.AddModelError(nameof(Username), "Username is required.");
        if (Password.Length < 12) ModelState.AddModelError(nameof(Password), "Password must be at least 12 characters long.");
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }
        await adminUserService.CreateLocalUserAsync(db, Username, Password, SelectedRoles, User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous", DateTimeOffset.UtcNow);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Users = await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync();
    }
}
