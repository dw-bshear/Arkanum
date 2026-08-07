namespace Arkanum.Tests;

public sealed class Issue024InstallerRuntimeHardeningTests
{
    [Fact]
    public void RuntimeSqlUserIsLeastPrivilegeAndSchemaChangesUseSetupIdentity()
    {
        var root = ProjectRoot();
        var provision = File.ReadAllText(Path.Combine(root, "installers", "sql", "provision-arkanum-sql.sql"));
        var schema = File.ReadAllText(Path.Combine(root, "installers", "sql", "arkanum-schema.sql"));

        Assert.Contains("Runtime least privilege", provision);
        Assert.Contains("GRANT SELECT", provision);
        Assert.Contains("GRANT INSERT", provision);
        Assert.Contains("GRANT UPDATE", provision);
        Assert.Contains("GRANT DELETE", provision);
        Assert.DoesNotContain("db_ddladmin", provision, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db_datawriter", provision, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db_datareader", provision, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Run arkanum-schema.sql with a setup/migration identity", provision);
        Assert.Contains("CREATE TABLE", schema);
        Assert.Contains("AuditEvents", schema);
    }

    [Fact]
    public void RuntimeStartupDoesNotPatchSqlSchemaAndInMemoryAvoidsRelationalSql()
    {
        var program = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Program.cs"));

        Assert.Contains("Migrate()", program);
        Assert.Contains("AllowRuntimeSchemaManagement", program);
        Assert.Contains("IsRelational()", program);
        Assert.DoesNotContain("IF COL_LENGTH", program);
        Assert.DoesNotContain("IF OBJECT_ID", program);
        Assert.DoesNotContain("Data Source=/srv/blackbox/hermes/projects/arkanum/data/arkanum-dev.db", program);
    }

    [Fact]
    public void InstallersProtectRuntimeSecretsAndRedactLogs()
    {
        var root = ProjectRoot();
        var windows = File.ReadAllText(Path.Combine(root, "installers", "windows", "Install-Arkanum.ps1"));
        var linux = File.ReadAllText(Path.Combine(root, "installers", "linux", "install-arkanum.sh"));

        Assert.Contains("ARKANUM_MASTER_KEY_FILE", windows);
        Assert.Contains("ConnectionStrings__DefaultConnection_FILE", windows);
        Assert.DoesNotContain("environmentVariable name=\"ARKANUM_MASTER_KEY\" value=\"$escapedMasterKey\"", windows);
        Assert.DoesNotContain("environmentVariable name=\"ConnectionStrings__DefaultConnection\" value=\"$escapedSqlConnectionString\"", windows);
        Assert.Contains("Protect-ArkanumSecretFile", windows);
        Assert.Contains("Redact-ArkanumSecret", windows);

        Assert.Contains("ARKANUM_MASTER_KEY_FILE", linux);
        Assert.Contains("ConnectionStrings__DefaultConnection_FILE", linux);
        Assert.DoesNotContain("ARKANUM_MASTER_KEY=$(cat", linux);
        Assert.DoesNotContain("ConnectionStrings__DefaultConnection=${ARKANUM_SQL_CONNECTION}", linux);
        Assert.Contains("redact_secret", linux);
    }

    [Fact]
    public void DocumentationExplainsProtectedConfigurationAndInstallerManagedSchema()
    {
        var root = ProjectRoot();
        var doc = File.ReadAllText(Path.Combine(root, "docs", "installer-runtime-hardening.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("setup/migration identity", doc);
        Assert.Contains("least-privilege runtime", doc);
        Assert.Contains("protected configuration", doc);
        Assert.Contains("redact", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("installer-runtime-hardening.md", readme);
    }

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find project root.");
    }
}
