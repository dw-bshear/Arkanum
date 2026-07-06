using Arkanum.Web.Services;
using Microsoft.Extensions.Configuration;

namespace Arkanum.Tests;

public class SiteRecoveryKeyStorageTests
{
    [Fact]
    public void Environment_master_key_provider_reads_local_key_file_when_environment_key_is_absent()
    {
        var keyFile = Path.Combine(Path.GetTempPath(), $"arkanum-key-{Guid.NewGuid():n}.txt");
        File.WriteAllText(keyFile, "ark-recovery-v1.local-file-key-material-for-tests");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Arkanum:EncryptionKeyEnvironmentVariable"] = "ARKANUM_TEST_MISSING_KEY",
                    ["Arkanum:MasterKeyFile"] = keyFile
                })
                .Build();

            var provider = new EnvironmentMasterKeyProvider(configuration);

            Assert.Equal(32, provider.GetMasterKey().Length);
            Assert.Equal("site:v1", provider.KeyId);
        }
        finally
        {
            File.Delete(keyFile);
        }
    }

    [Fact]
    public void Local_master_key_generation_creates_one_site_wide_recovery_key_and_reuses_it()
    {
        var keyFile = Path.Combine(Path.GetTempPath(), $"arkanum-key-{Guid.NewGuid():n}.txt");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Arkanum:EncryptionKeyEnvironmentVariable"] = "ARKANUM_TEST_MISSING_KEY",
                    ["Arkanum:MasterKeyFile"] = keyFile
                })
                .Build();

            var first = EnvironmentMasterKeyProvider.EnsureLocalMasterKey(configuration);
            var second = EnvironmentMasterKeyProvider.EnsureLocalMasterKey(configuration);

            Assert.NotNull(first);
            Assert.Null(second);
            Assert.Equal(first, File.ReadAllText(keyFile).Trim());
            Assert.True(SiteRecoveryKeyService.TryValidateRecoveryKeyFormat(first!, out var error), error);
        }
        finally
        {
            File.Delete(keyFile);
        }
    }

    [Fact]
    public void Setup_page_explains_site_wide_recovery_key_and_restore_requirement()
    {
        var markupPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Arkanum.Web", "Pages", "Setup.cshtml"));
        var markup = File.ReadAllText(markupPath);

        Assert.Contains("Arkanum Recovery Key", markup);
        Assert.Contains("site-wide", markup);
        Assert.Contains("all vault credentials and secure notes", markup);
        Assert.Contains("SQL Server backup", markup);
    }

    [Fact]
    public void Setup_page_requires_recovery_key_save_acknowledgment_before_admin_creation()
    {
        var markupPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Arkanum.Web", "Pages", "Setup.cshtml"));
        var pageModelPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Arkanum.Web", "Pages", "Setup.cshtml.cs"));
        var markup = File.ReadAllText(markupPath);
        var pageModel = File.ReadAllText(pageModelPath);

        Assert.Contains("asp-for=\"RecoveryKeySavedAcknowledged\"", markup);
        Assert.Contains("I have saved this recovery key", markup);
        Assert.Contains("Copy recovery key", markup);
        Assert.Contains("Download recovery key file", markup);
        Assert.Contains("RecoveryKeySavedAcknowledged", pageModel);
        Assert.Contains("You must acknowledge that the Arkanum Recovery Key has been saved", pageModel);
    }
}
