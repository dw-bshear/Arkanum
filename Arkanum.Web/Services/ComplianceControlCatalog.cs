namespace Arkanum.Web.Services;

public enum ComplianceControlStatus
{
    Implemented,
    Partial,
    Planned,
    Inherited
}

public sealed record ComplianceControlMapping(
    string Id,
    string Family,
    string Control,
    ComplianceControlStatus Status,
    string ArkanumEvidence,
    string Notes);

public static class ComplianceControlCatalog
{
    public static readonly IReadOnlyList<ComplianceControlMapping> Controls =
    [
        new("APP-AUTH-001", "Identification and Authentication", "IA-2 / IA-5 alignment", ComplianceControlStatus.Partial, "Local emergency login uses hashed passwords; account lockout is enforced; OIDC/SAML settings are modeled for enterprise IdP integration.", "Smart card/PIV can be handled upstream by the IdP for OIDC/SAML deployments. Direct smart-card auth is optional, not required for Phase 4."),
        new("APP-AC-001", "Access Control", "AC-2 / AC-3 / AC-6 alignment", ComplianceControlStatus.Implemented, "Role catalog, multi-role assignment, Administrator/Vault Manager/Auditor/User capability checks, and auditor-only read-only enforcement.", "Future production schema can normalize roles, but current role-set enforcement is tested."),
        new("APP-AU-001", "Audit and Accountability", "AU-2 / AU-3 / AU-6 alignment", ComplianceControlStatus.Implemented, "Login success/failure, account lockout, settings changes, vault assignment, secret create/reveal/copy, and syslog forwarding foundation.", "Audit events avoid plaintext secrets."),
        new("APP-SC-001", "System and Communications Protection", "SC-8 / SC-13 alignment", ComplianceControlStatus.Partial, "Security headers, no-store sensitive pages, SQL encryption setting, server-side AES-GCM secret encryption using master key.", "TLS termination and certificate policy remain deployment responsibilities."),
        new("APP-CM-001", "Configuration Management", "CM-6 / CM-8 alignment", ComplianceControlStatus.Partial, "Install report, health checks, package inventory, SHA-256 file manifest, documented controlled-network posture.", "Formal STIG checklist export and signed release artifacts remain planned."),
        new("APP-CP-001", "Contingency Planning", "CP-9 alignment", ComplianceControlStatus.Planned, "Backup/restore notes from earlier install planning.", "A tested backup/restore transcript is still needed."),
        new("APP-SI-001", "System and Information Integrity", "SI-2 / SI-4 alignment", ComplianceControlStatus.Partial, "Health endpoint, install report, syslog forwarding foundation, release evidence checksums.", "Patch-management process and vulnerability scanning evidence are deployment/program responsibilities.")
    ];

    public static IReadOnlyDictionary<ComplianceControlStatus, int> CountByStatus() =>
        Controls.GroupBy(control => control.Status).ToDictionary(group => group.Key, group => group.Count());
}
