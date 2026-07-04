using Arkanum.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Pages.Audit;

public class IndexModel(ArkanumDbContext db) : PageModel
{
    public IReadOnlyList<AuditEventEntity> Events { get; private set; } = [];
    public async Task OnGetAsync()
    {
        var events = await db.AuditEvents.AsNoTracking().ToListAsync();
        Events = events.OrderByDescending(e => e.OccurredAt).Take(100).ToArray();
    }
}
