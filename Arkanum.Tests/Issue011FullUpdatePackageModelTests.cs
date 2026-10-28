namespace Arkanum.Tests;

public class Issue011FullUpdatePackageModelTests
{
    [Fact]
    public void ReleasePackagingScriptDefinesFullAndUpdateAssetsWithManifestsForBothPlatforms()
    {
        var root = ProjectRoot();
        var scriptPath = Path.Combine(root, "scripts", "create-release-packages.py");

        Assert.True(File.Exists(scriptPath), "A first-class release packaging script should build full and update assets.");
        var script = File.ReadAllText(scriptPath);

        foreach (var expected in new[]
        {
            "Arkanum-{version}-full-windows-x64.zip",
            "Arkanum-{version}-update-windows-x64.zip",
            "Arkanum-{version}-full-linux-x64.tar.gz",
            "Arkanum-{version}-update-linux-x64.tar.gz",
            "package-manifest.json",
            "requiresExistingInstall",
            "minimumInstalledVersion",
            "recovery-master-key",
            "database-connectivity-settings",
            "service-binding-settings"
        })
        {
            Assert.Contains(expected, script);
        }
    }

    [Fact]
    public void WindowsAndLinuxInstallersTreatUpdatePackagesAsExistingInstallOnlyPreflightableUpgrades()
    {
        var root = ProjectRoot();
        var windows = File.ReadAllText(Path.Combine(root, "installers", "windows", "Install-Arkanum.ps1"));
        var linux = File.ReadAllText(Path.Combine(root, "installers", "linux", "install-arkanum.sh"));

        Assert.Contains("PackageKind", windows);
        Assert.Contains("ValidateSet('full','update','auto')", windows);
        Assert.Contains("PreflightOnly", windows);
        Assert.Contains("package-manifest.json", windows);
        Assert.Contains("Update package requires an existing Arkanum installation", windows);
        Assert.Contains("Update package will preserve existing configuration, Recovery Key, database connection, and service binding settings", windows);
        Assert.Contains("Current version:", windows);
        Assert.Contains("Target version:", windows);
        Assert.Contains("PreflightOnly", windows);
        Assert.Contains("existingSqlConnectionPath", windows);

        Assert.Contains("PACKAGE_KIND", linux);
        Assert.Contains("PREFLIGHT_ONLY", linux);
        Assert.Contains("package-manifest.json", linux);
        Assert.Contains("Update package requires an existing Arkanum installation", linux);
        Assert.Contains("Update package will preserve existing configuration, Recovery Key, database connection, and service binding settings", linux);
        Assert.Contains("Current version:", linux);
        Assert.Contains("Target version:", linux);
        Assert.Contains("EXISTING_SQL_CONNECTION_FILE", linux);
    }

    [Fact]
    public void DocumentationExplainsWhichAssetToUseAndControlledEnvironmentUpgradeWorkflow()
    {
        var root = ProjectRoot();
        var guide = File.ReadAllText(Path.Combine(root, "docs", "upgrade-and-native-packaging.md"));
        var checklist = File.ReadAllText(Path.Combine(root, "docs", "offline-package-checklist.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        foreach (var doc in new[] { guide, checklist, readme })
        {
            Assert.Contains("full", doc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("update", doc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Arkanum-1.1.1-full-windows-x64.zip", doc);
            Assert.Contains("Arkanum-1.1.1-update-windows-x64.zip", doc);
            Assert.Contains("Arkanum-1.1.1-full-linux-x64.tar.gz", doc);
            Assert.Contains("Arkanum-1.1.1-update-linux-x64.tar.gz", doc);
        }

        Assert.Contains("Run preflight before the maintenance window", guide);
        Assert.Contains("SQL database backup + matching Arkanum Recovery Key", guide);
        Assert.Contains("Current version:", guide);
        Assert.Contains("Target version:", guide);
        Assert.Contains("No SQL passwords or Recovery Key values are written to this report", guide);
    }

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find project root.");
    }
}
