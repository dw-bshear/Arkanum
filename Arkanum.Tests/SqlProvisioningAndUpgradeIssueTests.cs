namespace Arkanum.Tests;

public class SqlProvisioningAndUpgradeIssueTests
{
    [Fact]
    public void SqlProvisioningScriptsCreateDatabaseRuntimeLoginAndLeastPrivilegeUser()
    {
        var root = ProjectRoot();
        var provision = File.ReadAllText(Path.Combine(root, "installers", "sql", "provision-arkanum-sql.sql"));

        Assert.Contains("CREATE DATABASE", provision);
        Assert.Contains("CREATE LOGIN", provision);
        Assert.Contains("CREATE USER", provision);
        Assert.Contains("ALTER ROLE [db_datareader] ADD MEMBER", provision);
        Assert.Contains("ALTER ROLE [db_datawriter] ADD MEMBER", provision);
        Assert.Contains("ALTER ROLE [db_ddladmin] ADD MEMBER", provision);
        Assert.DoesNotContain("sysadmin", provision, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serveradmin", provision, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsAndLinuxInstallersExposeCreateDatabaseModeWithoutPersistingSetupCredentials()
    {
        var root = ProjectRoot();
        var windows = File.ReadAllText(Path.Combine(root, "installers", "windows", "Install-Arkanum.ps1"));
        var windowsGui = File.ReadAllText(Path.Combine(root, "installers", "windows", "Install-ArkanumGui.ps1"));
        var linux = File.ReadAllText(Path.Combine(root, "installers", "linux", "install-arkanum.sh"));
        var linuxGui = File.ReadAllText(Path.Combine(root, "installers", "linux", "arkanum-gui-install.py"));

        foreach (var script in new[] { windows, linux })
        {
            Assert.Contains("create-new-database", script);
            Assert.Contains("use-existing-database", script);
            Assert.Contains("runtime SQL connection", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("setup/admin SQL credential is used only during provisioning", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SQL setup credential persistence: not persisted", script);
            Assert.Contains("Invoke-ArkanumSqlProvisioning", script);
        }

        Assert.Contains("Database setup mode", windowsGui);
        Assert.Contains("Create new database", windowsGui);
        Assert.Contains("Setup/admin SQL username", windowsGui);
        Assert.Contains("Runtime app login", windowsGui);
        Assert.Contains("Generate runtime password", windowsGui);

        Assert.Contains("database_setup_mode", linuxGui);
        Assert.Contains("Create new database", linuxGui);
        Assert.Contains("setup_sql_user", linuxGui);
        Assert.Contains("runtime_sql_user", linuxGui);
        Assert.Contains("generate_runtime_password", linuxGui);
    }

    [Fact]
    public void UpgradePreflightScriptsValidateReachabilitySchemaPermissionsBackupAndFailureMessages()
    {
        var root = ProjectRoot();
        var preflight = File.ReadAllText(Path.Combine(root, "installers", "sql", "upgrade-preflight.sql"));
        var windows = File.ReadAllText(Path.Combine(root, "installers", "windows", "Install-Arkanum.ps1"));
        var linux = File.ReadAllText(Path.Combine(root, "installers", "linux", "install-arkanum.sh"));

        Assert.Contains("__EFMigrationsHistory", preflight);
        Assert.Contains("SELECT DB_NAME()", preflight);
        Assert.Contains("HAS_PERMS_BY_NAME", preflight);
        Assert.Contains("ALTER", preflight);
        Assert.Contains("INSERT", preflight);
        Assert.Contains("UPDATE", preflight);
        Assert.Contains("DELETE", preflight);

        foreach (var script in new[] { windows, linux })
        {
            Assert.Contains("Invoke-ArkanumSqlUpgradePreflight", script);
            Assert.Contains("Database reachability preflight", script);
            Assert.Contains("Schema/version preflight", script);
            Assert.Contains("Runtime login permission preflight", script);
            Assert.Contains("SQL backup strongly recommended before upgrade", script);
            Assert.Contains("permission problem", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("database reachability", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("schema mismatch", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("recovery-key mismatch", script, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SqlInstallerDocumentationExplainsProvisioningUpgradeAndNoSecretReports()
    {
        var root = ProjectRoot();
        var doc = File.ReadAllText(Path.Combine(root, "docs", "initial-sql-setup-notes.md"));

        Assert.Contains("Database setup mode", doc);
        Assert.Contains("Use existing database", doc);
        Assert.Contains("Create new database", doc);
        Assert.Contains("setup/admin SQL credential is used only during installation", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Persist only the runtime SQL connection", doc);
        Assert.Contains("limited runtime login", doc);
        Assert.Contains("SQL upgrade preflight", doc);
        Assert.Contains("pre-upgrade backup", doc);
        Assert.Contains("rollback guidance", doc);
        Assert.Contains("Reports must not include SQL passwords, setup credentials, runtime passwords, or Recovery Keys", doc);
    }

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find project root.");
    }
}
