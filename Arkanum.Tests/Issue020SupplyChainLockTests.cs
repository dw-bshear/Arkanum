using System.Xml.Linq;
using Xunit;

namespace Arkanum.Tests;

public class Issue020SupplyChainLockTests
{
    [Fact]
    public void ProjectsPinNonVulnerableSqliteNativePackageAndUseLockedRestore()
    {
        var root = ProjectRoot();
        var webProject = XDocument.Load(Path.Combine(root, "Arkanum.Web", "Arkanum.Web.csproj"));
        var propsText = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));

        var sqliteNative = webProject.Descendants("PackageReference")
            .SingleOrDefault(e => string.Equals((string?)e.Attribute("Include"), "SQLitePCLRaw.lib.e_sqlite3", StringComparison.Ordinal));

        Assert.NotNull(sqliteNative);
        Assert.True(Version.TryParse((string?)sqliteNative.Attribute("Version"), out var version), "SQLitePCLRaw.lib.e_sqlite3 must have an explicit version.");
        Assert.True(version >= new Version(3, 53, 3), $"SQLitePCLRaw.lib.e_sqlite3 must be at least 3.53.3; found {version}.");
        Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", propsText);
        Assert.Contains("<RestoreLockedMode Condition=", propsText);
    }

    [Fact]
    public void PackageLocksExistForWebAndTestProjectsAndExcludeVulnerableSqliteNativeVersion()
    {
        var root = ProjectRoot();
        var webLockPath = Path.Combine(root, "Arkanum.Web", "packages.lock.json");
        var testLockPath = Path.Combine(root, "Arkanum.Tests", "packages.lock.json");

        Assert.True(File.Exists(webLockPath), "Arkanum.Web/packages.lock.json is required for offline locked restore evidence.");
        Assert.True(File.Exists(testLockPath), "Arkanum.Tests/packages.lock.json is required for offline locked restore evidence.");
        var lockText = File.ReadAllText(webLockPath) + File.ReadAllText(testLockPath);

        Assert.Contains("SQLitePCLRaw.lib.e_sqlite3", lockText);
        Assert.DoesNotContain("\"SQLitePCLRaw.lib.e_sqlite3\": {\n        \"type\": \"Transitive\",\n        \"resolved\": \"2.1.10\"", lockText);
        Assert.Contains("\"resolved\": \"3.53.3\"", lockText);
    }

    [Fact]
    public void ReleaseEvidenceScriptCapturesSbomVulnerabilityScanLockFilesAndChecksums()
    {
        var root = ProjectRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "generate-phase3-release-evidence.sh"));

        Assert.Contains("dotnet restore --locked-mode", script);
        Assert.Contains("dotnet list \"$ROOT/Arkanum.sln\" package --vulnerable --include-transitive", script);
        Assert.Contains("CycloneDX", script);
        Assert.Contains("packages.lock.json", script);
        Assert.Contains("SHA256SUMS.txt", script);
    }

    [Fact]
    public void OfflineChecklistDocumentsNugetLockSbomChecksumsAndVulnerabilityEvidence()
    {
        var root = ProjectRoot();
        var checklist = File.ReadAllText(Path.Combine(root, "docs", "offline-package-checklist.md"));

        Assert.Contains("packages.lock.json", checklist);
        Assert.Contains("dotnet restore --locked-mode", checklist);
        Assert.Contains("CycloneDX", checklist);
        Assert.Contains("SBOM", checklist);
        Assert.Contains("dotnet list Arkanum.sln package --vulnerable --include-transitive", checklist);
        Assert.Contains("SHA-256", checklist);
        Assert.Contains("offline NuGet", checklist, StringComparison.OrdinalIgnoreCase);
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
