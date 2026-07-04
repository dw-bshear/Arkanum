using Arkanum.Web.Domain;

namespace Arkanum.Web.Data;

public static class DashboardWidgetKeys
{
    public const string Vaults = "vaults";
    public const string UserManagement = "user-management";
    public const string AuditLogs = "audit-logs";
    public const string DirectorySync = "directory-sync";
    public const string PasswordPolicy = "password-policy";
}

public sealed class DashboardWidgetEntity
{
    public string WidgetKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public string RolesCsv { get; set; } = string.Empty;

    public bool IsVisibleTo(IReadOnlyCollection<ArkanumRole> roles)
    {
        var allowed = RolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Enum.Parse<ArkanumRole>)
            .ToHashSet();
        return Enabled && roles.Any(allowed.Contains);
    }
}
