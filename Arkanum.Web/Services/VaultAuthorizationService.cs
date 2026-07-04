using Arkanum.Web.Domain;

namespace Arkanum.Web.Services;

public sealed class VaultAuthorizationService
{
    public bool CanRevealAnySecret(ArkanumUser user) => ArkanumRoleCapabilities.CanRevealSecrets(user.Roles);
    public bool CanExportAuditLogs(ArkanumUser user) => ArkanumRoleCapabilities.CanExportAuditLogs(user.Roles);

    public bool CanEditVault(ArkanumUser user, bool assignedVaultManager) =>
        ArkanumRoleCapabilities.CanManageAnyVault(user.Roles) ||
        (assignedVaultManager && user.Roles.Contains(ArkanumRole.VaultManager));
}
