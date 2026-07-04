namespace Arkanum.Web.Domain;

public enum AuditEventType
{
    FirstAdminCreated = 0,
    LoginSucceeded = 1,
    LoginFailed = 2,
    DirectorySync = 3,
    UserEnabled = 4,
    UserDisabled = 5,
    RoleChanged = 6,
    VaultAssignmentChanged = 7,
    SecretCreated = 8,
    SecretUpdated = 9,
    SecretDeleted = 10,
    SecretReveal = 11,
    SecretCopy = 12,
    SettingsChanged = 13,
    AccountLocked = 14
}

public sealed record AuditEvent(string Id, AuditEventType Type, string ActorUserKey, DateTimeOffset OccurredAt)
{
    public string Target { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
