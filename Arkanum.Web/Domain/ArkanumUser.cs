namespace Arkanum.Web.Domain;

public sealed class ArkanumUser
{
    public string UserKey { get; set; } = Guid.NewGuid().ToString("n");
    public string? ExternalId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserSource Source { get; set; }
    public bool DirectoryPresent { get; set; }
    public bool AppAccessEnabled { get; set; }
    public string DisabledReason { get; set; } = string.Empty;
    public DateTimeOffset? LastDirectorySyncAt { get; set; }
    public List<ArkanumRole> Roles { get; set; } = [];

    public bool CanLogin => Source == UserSource.Local
        ? AppAccessEnabled
        : DirectoryPresent && AppAccessEnabled;
}
