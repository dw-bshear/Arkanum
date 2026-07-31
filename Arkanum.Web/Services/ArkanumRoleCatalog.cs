using Arkanum.Web.Domain;

namespace Arkanum.Web.Services;

public sealed record ArkanumRoleDescription(ArkanumRole Role, string DisplayName, string Summary);

public static class ArkanumRoleCatalog
{
    public static readonly IReadOnlyList<ArkanumRoleDescription> Descriptions =
    [
        new(ArkanumRole.Administrator, "Administrator", "Full system administration: settings, users, roles, directory, health/evidence review, and vault oversight."),
        new(ArkanumRole.VaultManager, "Vault Manager", "Operational vault stewardship: create and manage vaults, folders, access, and entries without access to system-wide settings."),
        new(ArkanumRole.Auditor, "Auditor", "Read-only oversight: review vault inventory and audit history without revealing or changing protected secret values."),
        new(ArkanumRole.User, "User", "Day-to-day vault use: open assigned vaults, add entries to accessible vaults, and reveal/copy secrets when authorized by vault membership.")
    ];

    public static string Describe(ArkanumRole role) => Descriptions.Single(d => d.Role == role).Summary;

    public static ArkanumRole? ParseClaimRole(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        if (!Enum.GetNames<ArkanumRole>().Contains(trimmed, StringComparer.OrdinalIgnoreCase)) return null;

        return Enum.Parse<ArkanumRole>(trimmed, ignoreCase: true);
    }

    public static IReadOnlyCollection<ArkanumRole> Normalize(IReadOnlyCollection<ArkanumRole> roles)
    {
        var normalized = roles.Count == 0 ? [ArkanumRole.User] : roles.Distinct().OrderBy(r => r.ToString()).ToList();
        if (!normalized.Contains(ArkanumRole.User)) normalized.Add(ArkanumRole.User);
        return normalized.Distinct().OrderBy(r => r.ToString()).ToArray();
    }
}

public static class ArkanumRoleCapabilities
{
    public static bool IsAdministrator(IReadOnlyCollection<ArkanumRole> roles) => roles.Contains(ArkanumRole.Administrator);
    public static bool IsVaultManager(IReadOnlyCollection<ArkanumRole> roles) => roles.Contains(ArkanumRole.VaultManager) || IsAdministrator(roles);
    public static bool IsAuditorOnly(IReadOnlyCollection<ArkanumRole> roles) => roles.Contains(ArkanumRole.Auditor) && roles.All(role => role == ArkanumRole.Auditor || role == ArkanumRole.User);
    public static bool CanCreateVault(IReadOnlyCollection<ArkanumRole> roles) => roles.Contains(ArkanumRole.User) && !IsAuditorOnly(roles);
    public static bool CanManageAnyVault(IReadOnlyCollection<ArkanumRole> roles) => IsVaultManager(roles);
    public static bool CanReviewAllVaults(IReadOnlyCollection<ArkanumRole> roles) => IsAdministrator(roles) || roles.Contains(ArkanumRole.VaultManager) || roles.Contains(ArkanumRole.Auditor);
    public static bool CanRevealSecrets(IReadOnlyCollection<ArkanumRole> roles) => roles.Contains(ArkanumRole.User) && !IsAuditorOnly(roles);
    public static bool CanExportAuditLogs(IReadOnlyCollection<ArkanumRole> roles) => roles.Contains(ArkanumRole.Auditor) || IsAdministrator(roles);
    public static bool CanManageUsersAndSettings(IReadOnlyCollection<ArkanumRole> roles) => IsAdministrator(roles);
}
