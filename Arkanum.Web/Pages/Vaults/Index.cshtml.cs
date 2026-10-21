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
    [BindProperty] public Guid RetireVaultId { get; set; }
    [BindProperty] public string? RetireConfirmationName { get; set; }
    public IReadOnlyList<VaultSummary> Vaults { get; private set; } = [];
    public bool IsStandardUser { get; private set; }
    public bool CanCreateVault { get; private set; }
    public bool CanRetireVaults { get; private set; }

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
        if (string.IsNullOrWhiteSpace(Description)) ModelState.AddModelError(nameof(Description), "Vault description is required.");
        if (!ModelState.IsValid)
        {
            await EnsureStandardPersonalVaultAsync();
            Vaults = await repository.ListVaultsForUserAsync(db, ActorKey(), CurrentRoles());
            return Page();
        }
        await repository.CreateVaultAsync(db, Name, Description, ActorKey(), DateTimeOffset.UtcNow);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetireAsync()
    {
        var roles = CurrentRoles();
        if (!ArkanumRoleCapabilities.IsAdministrator(roles)) return Forbid();
        try
        {
            await repository.RetireVaultAsync(db, RetireVaultId, ActorKey(), roles, RetireConfirmationName ?? string.Empty, DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(RetireConfirmationName), ex.Message);
            await EnsureStandardPersonalVaultAsync();
            Vaults = await repository.ListVaultsForUserAsync(db, ActorKey(), roles);
            return Page();
        }
        return RedirectToPage();
    }

    private async Task EnsureStandardPersonalVaultAsync()
    {
        var roles = CurrentRoles();
        IsStandardUser = roles.SetEquals([ArkanumRole.User]);
        CanCreateVault = ArkanumRoleCapabilities.CanCreateVault(roles) || ArkanumRoleCapabilities.CanManageAnyVault(roles);
        CanRetireVaults = ArkanumRoleCapabilities.IsAdministrator(roles);
        if (User?.Identity?.IsAuthenticated == true && IsStandardUser)
        {
            await repository.EnsurePersonalVaultAsync(db, ActorKey(), User.Identity.Name ?? "User", DateTimeOffset.UtcNow);
        }
    }

    private HashSet<ArkanumRole> CurrentRoles()
    {
        if (User?.Identity?.IsAuthenticated != true) return [];

        var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role)
            .Select(c => ArkanumRoleCatalog.ParseClaimRole(c.Value))
            .OfType<ArkanumRole>()
            .ToHashSet();
        return roles;
    }

    private string ActorKey() => User?.Identity?.IsAuthenticated == true
        ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException("Authenticated principal is missing a stable user key.")
        : throw new UnauthorizedAccessException("Authenticated principal is required.");
}
