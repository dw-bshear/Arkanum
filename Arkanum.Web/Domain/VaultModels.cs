namespace Arkanum.Web.Domain;

public enum VaultItemType
{
    LoginCredential,
    SecureNote
}

public sealed class Vault
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<VaultFolder> Folders { get; set; } = [];
}

public sealed class VaultFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VaultId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<VaultItem> Items { get; set; } = [];
}

public sealed class VaultItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VaultId { get; set; }
    public Guid? FolderId { get; set; }
    public VaultItemType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string EncryptedSecret { get; set; } = string.Empty;
    public string EncryptionKeyId { get; set; } = "env:v1";
    public string EncryptionAlgorithm { get; set; } = "AES-256-GCM";
    public DateTimeOffset EncryptedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateOnly? ResetNotificationDate { get; set; }
    public DateTimeOffset? ResetNotificationSentAt { get; set; }
}
