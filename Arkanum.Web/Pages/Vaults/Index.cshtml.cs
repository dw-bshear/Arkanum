using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Arkanum.Web.Pages.Vaults;

public class IndexModel(VaultRepository repository, Arkanum.Web.Data.ArkanumDbContext db) : PageModel
{
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string Description { get; set; } = string.Empty;
    public IReadOnlyList<VaultSummary> Vaults { get; private set; } = [];
    public bool IsStandardUser { get; private set; }
    public bool CanCreateVault { get; private set; }

    public async Task OnGetAsync()
    {
        await EnsureStandardPersonalVaultAsync();
        Vaults = await repository.ListVaultsForUserAsync(db, ActorKey(), CurrentRoles());
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var roles = CurrentRoles();
        if (!ArkanumRoleCapabilities.CanCreateVault(roles) && !ArkanumRoleCapabilities.CanManageAnyVault(roles)) return Forbid();
        if (string.IsNullOrWhiteSpace(Name)) ModelState.AddModelError(nameof(Name), "Vault name is required.");
        if (!ModelState.IsValid)
        {
            await EnsureStandardPersonalVaultAsync();
            Vaults = await repository.ListVaultsForUserAsync(db, ActorKey(), CurrentRoles());
            return Page();
        }
        await repository.CreateVaultAsync(db, Name, Description, ActorKey(), DateTimeOffset.UtcNow);
        return RedirectToPage();
    }

    private async Task EnsureStandardPersonalVaultAsync()
    {
        var roles = CurrentRoles();
        IsStandardUser = roles.SetEquals([ArkanumRole.User]);
        CanCreateVault = ArkanumRoleCapabilities.CanCreateVault(roles) || ArkanumRoleCapabilities.CanManageAnyVault(roles);
        if (User?.Identity?.IsAuthenticated == true && IsStandardUser)
        {
            await repository.EnsurePersonalVaultAsync(db, ActorKey(), User.Identity.Name ?? "User", DateTimeOffset.UtcNow);
        }
    }

    private HashSet<ArkanumRole> CurrentRoles()
    {
        var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role)
            .Select(c => Enum.TryParse<ArkanumRole>(c.Value, out var role) ? role : (ArkanumRole?)null)
            .OfType<ArkanumRole>()
            .ToHashSet();
        return roles.Count == 0 ? [ArkanumRole.User] : roles;
    }

    private string ActorKey() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
}
