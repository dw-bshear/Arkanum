namespace Arkanum.Web.Data;

public sealed class PortalSettingEntity
{
    public string SettingKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedByUserKey { get; set; } = string.Empty;
}
