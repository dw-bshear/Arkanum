using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed class DashboardService
{
    public async Task EnsureDefaultWidgetsAsync(ArkanumDbContext db)
    {
        if (await db.DashboardWidgets.AnyAsync()) return;
        db.DashboardWidgets.AddRange(DefaultWidgets());
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<DashboardWidgetEntity>> GetVisibleWidgetsAsync(ArkanumDbContext db, IReadOnlyCollection<ArkanumRole> roles)
    {
        await EnsureDefaultWidgetsAsync(db);
        var widgets = await db.DashboardWidgets.AsNoTracking().ToListAsync();
        return widgets.Where(w => w.IsVisibleTo(roles)).OrderBy(w => w.SortOrder).ToArray();
    }

    public async Task<IReadOnlyList<DashboardWidgetEntity>> GetAllWidgetsAsync(ArkanumDbContext db)
    {
        await EnsureDefaultWidgetsAsync(db);
        return await db.DashboardWidgets.AsNoTracking().OrderBy(w => w.SortOrder).ToListAsync();
    }

    public async Task UpdateWidgetAsync(ArkanumDbContext db, string widgetKey, bool enabled, string title, string description, string rolesCsv, int sortOrder)
    {
        var widget = await db.DashboardWidgets.SingleAsync(w => w.WidgetKey == widgetKey);
        widget.Enabled = enabled;
        widget.Title = title.Trim();
        widget.Description = description.Trim();
        widget.RolesCsv = rolesCsv.Trim();
        widget.SortOrder = sortOrder;
        await db.SaveChangesAsync();
    }

    public static IReadOnlyList<DashboardWidgetEntity> DefaultWidgets() =>
    [
        new() { WidgetKey = DashboardWidgetKeys.Vaults, Title = "Vault access", Description = "Open assigned vaults and review stored items.", Enabled = true, SortOrder = 10, RolesCsv = "User,VaultManager,Auditor,Administrator" },
        new() { WidgetKey = DashboardWidgetKeys.UserManagement, Title = "User management", Description = "Create local users, review imported users, and adjust access.", Enabled = true, SortOrder = 20, RolesCsv = "Administrator" },
        new() { WidgetKey = DashboardWidgetKeys.AuditLogs, Title = "Audit logs", Description = "Review access history and secret access events.", Enabled = true, SortOrder = 30, RolesCsv = "Auditor,Administrator" },
        new() { WidgetKey = DashboardWidgetKeys.DirectorySync, Title = "Directory sources", Description = "Configure LDAP/AD sources and sync imported users.", Enabled = true, SortOrder = 40, RolesCsv = "Administrator" },
        new() { WidgetKey = DashboardWidgetKeys.PasswordPolicy, Title = "Password policy", Description = "Tune password generator settings and minimum requirements.", Enabled = true, SortOrder = 50, RolesCsv = "Administrator,VaultManager" }
    ];
}
