namespace Arkanum.Tests;

public class RestoreWorkflowIssueTests
{
    [Fact]
    public void WindowsInstallerDefinesExplicitRestoreSourcesAndRejectsDeferredSqlPaths()
    {
        var script = File.ReadAllText(Path.Combine(ProjectRoot(), "installers", "windows", "Install-Arkanum.ps1"));
        var gui = File.ReadAllText(Path.Combine(ProjectRoot(), "installers", "windows", "Install-ArkanumGui.ps1"));

        Assert.Contains("[ValidateSet('dba-restored-database','sql-backup-file','attach-database-files')]", script);
        Assert.Contains("[string]$RestoreSource = 'dba-restored-database'", script);
        Assert.Contains("[string]$SqlBackupFile = ''", script);
        Assert.Contains("Use database already restored by DBA", gui);
        Assert.Contains("Restore from SQL backup file", gui);
        Assert.Contains("Attach existing database files / DBA handoff", gui);
        Assert.Contains("SQL backup and attach restore paths are staged for DBA workflow selection but not automated in this release", script);
        Assert.Contains("For this release, restore the SQL database first, then choose dba-restored-database", script);
    }

    [Fact]
    public void LinuxInstallerDefinesExplicitRestoreSourcesAndRejectsDeferredSqlPaths()
    {
        var script = File.ReadAllText(Path.Combine(ProjectRoot(), "installers", "linux", "install-arkanum.sh"));
        var gui = File.ReadAllText(Path.Combine(ProjectRoot(), "installers", "linux", "arkanum-gui-install.py"));

        Assert.Contains("RESTORE_SOURCE=${RESTORE_SOURCE:-dba-restored-database}", script);
        Assert.Contains("dba-restored-database|sql-backup-file|attach-database-files", script);
        Assert.Contains("SQL_BACKUP_FILE", script);
        Assert.Contains("Use database already restored by DBA", gui);
        Assert.Contains("Restore from SQL backup file", gui);
        Assert.Contains("Attach existing database files / DBA handoff", gui);
        Assert.Contains("SQL backup and attach restore paths are staged for DBA workflow selection but not automated in this release", script);
        Assert.Contains("For this release, restore the SQL database first, then choose RESTORE_SOURCE=dba-restored-database", script);
    }

    [Fact]
    public void RestoreReportsIncludeSourceKeyValidationAndNoSecretGuarantees()
    {
        var root = ProjectRoot();
        var windows = File.ReadAllText(Path.Combine(root, "installers", "windows", "Install-Arkanum.ps1"));
        var linux = File.ReadAllText(Path.Combine(root, "installers", "linux", "install-arkanum.sh"));

        foreach (var script in new[] { windows, linux })
        {
            Assert.Contains("Restore source", script);
            Assert.Contains("Recovery Key source", script);
            Assert.Contains("Recovery sentinel validation", script);
            Assert.Contains("Recoverable state requires both the SQL database backup and the matching Arkanum Recovery Key", script);
            Assert.Contains("No SQL passwords or Recovery Key values are written to this report", script);
            Assert.Contains("No new Recovery Key was generated", script);
        }

        Assert.Contains("Restore report", windows);
        Assert.Contains("restore-report-$PackageVersion.txt", windows);
        Assert.Contains("restore-report-${ARKANUM_PACKAGE_VERSION}.txt", linux);
    }

    [Fact]
    public void BackupRestoreDocsDescribeMinimumMaterialsAndSupportedRestorePaths()
    {
        var doc = File.ReadAllText(Path.Combine(ProjectRoot(), "docs", "backup-and-restore.md"));

        Assert.Contains("Minimum materials needed to recover an Arkanum instance", doc);
        Assert.Contains("SQL database backup or DBA-restored Arkanum database", doc);
        Assert.Contains("matching Arkanum Recovery Key", doc);
        Assert.Contains("Use database already restored by DBA", doc);
        Assert.Contains("Restore from SQL backup file", doc);
        Assert.Contains("Attach existing database files / manual DBA handoff", doc);
        Assert.Contains("Currently supported", doc);
        Assert.Contains("Planned/future", doc);
        Assert.Contains("The installer does not generate a new Recovery Key in restore mode", doc);
        Assert.Contains("The provided Arkanum Recovery Key does not match the restored database", doc);
    }

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find project root.");
    }
}
