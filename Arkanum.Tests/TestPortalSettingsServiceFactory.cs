using Arkanum.Web.Services;

namespace Arkanum.Tests;

public static class TestPortalSettingsServiceFactory
{
    public static PortalSettingsService Create() => new(new SecretEncryptionService(new StaticMasterKeyProvider("portal-settings-test-master-key-material")));
}
