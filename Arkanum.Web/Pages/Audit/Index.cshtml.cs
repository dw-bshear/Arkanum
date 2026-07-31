using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Arkanum.Web.Pages.Audit;

public class IndexModel(ArkanumDbContext db) : PageModel
{
    public IReadOnlyList<AuditEventEntity> Events { get; private set; } = [];
    public async Task OnGetAsync()
    {
        var events = await db.AuditEvents.AsNoTracking().ToListAsync();
        Events = events.OrderByDescending(e => e.OccurredAt).Take(100).ToArray();
    }

    public async Task<IActionResult> OnPostExportCsvAsync(CancellationToken cancellationToken = default)
    {
        if (!ArkanumRoleCapabilities.CanExportAuditLogs(CurrentRoles())) return Forbid();

        var export = await new AuditReportExportService().ExportAuditCsvAsync(db, ActorKey(), DateTimeOffset.UtcNow, cancellationToken);
        return File(export.Content, export.ContentType, export.FileName);
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
