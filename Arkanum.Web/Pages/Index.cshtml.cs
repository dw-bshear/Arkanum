using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Arkanum.Web.Pages;

public class IndexModel(ArkanumDbContext db, FirstRunSetupService setupService, VaultRepository vaultRepository, PortalSettingsService portalSettings) : PageModel
{
    public int VaultCount { get; private set; }
    public int RecentAuditCount { get; private set; }
    public bool SetupRequired { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public bool IsStandardUser { get; private set; }
    public IReadOnlyList<VaultSummary> UserVaults { get; private set; } = [];
    public DashboardTextSettings DashboardText { get; private set; } = new("Your starting point for vault work.", "Open the vaults available to you and keep shared credentials organized without turning the landing page into an administration console.");

    public async Task<IActionResult> OnGetAsync()
    {
        SetupRequired = setupService.IsSetupRequired(await setupService.LoadUsersAsync(db));
        IsAuthenticated = User?.Identity?.IsAuthenticated == true;

        if (!IsAuthenticated)
        {
            return Page();
        }

        VaultCount = await db.Vaults.CountAsync();
        RecentAuditCount = await db.AuditEvents.CountAsync();
        DashboardText = await portalSettings.GetDashboardTextAsync(db);

        var roles = CurrentRoles();
        IsStandardUser = roles.SetEquals([ArkanumRole.User]);
        var userKey = ActorKey();
        if (IsStandardUser)
        {
            await vaultRepository.EnsurePersonalVaultAsync(db, userKey, User?.Identity?.Name ?? "User", DateTimeOffset.UtcNow);
        }
        UserVaults = await vaultRepository.ListVaultsForUserAsync(db, userKey, roles);

        return Page();
    }

    private HashSet<ArkanumRole> CurrentRoles()
    {
        if (User?.Identity?.IsAuthenticated != true) return [];

        var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role)
            .Select(c => Enum.TryParse<ArkanumRole>(c.Value, out var role) ? role : (ArkanumRole?)null)
            .OfType<ArkanumRole>()
            .ToHashSet();
        return roles;
    }

    private string ActorKey() => User?.Identity?.IsAuthenticated == true
        ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException("Authenticated principal is missing a stable user key.")
        : throw new UnauthorizedAccessException("Authenticated principal is required.");
}
