using System.IO;
using Xunit;

namespace Arkanum.Tests;

public class ConclaveBrandingTests
{
    [Fact]
    public void ProductVersionAndReleaseDocsUseConclaveVersionSequence()
    {
        var root = ProjectRoot();
        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var notesPath = Path.Combine(root, "docs", "conclave-2026.0.1-release.md");

        Assert.Contains("<Version>2026.0.1</Version>", props);
        Assert.Contains("<InformationalVersion>2026.0.1-conclave-frozen-fork</InformationalVersion>", props);
        Assert.Contains("# Conclave", readme);
        Assert.Contains("derived from Arkanum 2026.0.6", readme);
        Assert.True(File.Exists(notesPath), "Conclave release notes should exist.");
        Assert.Contains("Conclave 2026.0.1", File.ReadAllText(notesPath));
    }

    [Fact]
    public void PublicFacingInstallersAndDocsUseConclaveBranding()
    {
        var root = ProjectRoot();
        var files = new[]
        {
            "README.md",
            "installers/windows/Install-Arkanum.ps1",
            "installers/windows/Install-ArkanumGui.ps1",
            "installers/windows/Build-ArkanumMsi.ps1",
            "installers/linux/install-arkanum.sh",
            "installers/linux/arkanum-gui-install.py",
            "installers/linux/build-native-package.sh",
            "docs/conclave-2026.0.1-release.md"
        };

        foreach (var relative in files)
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            Assert.Contains("Conclave", text);
            Assert.Contains("2026.0.1", text);
        }

        var windowsInstaller = File.ReadAllText(Path.Combine(root, "installers/windows/Install-Arkanum.ps1"));
        var linuxInstaller = File.ReadAllText(Path.Combine(root, "installers/linux/install-arkanum.sh"));
        Assert.Contains("C:\\ProgramData\\Conclave", windowsInstaller);
        Assert.Contains("/opt/conclave", linuxInstaller);
        Assert.Contains("/etc/conclave", linuxInstaller);
    }

    [Fact]
    public void ConclaveBranchDoesNotLinkToArkanumProductReleaseNotesAsCurrentRelease()
    {
        var root = ProjectRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("docs/conclave-2026.0.1-release.md", readme);
        Assert.DoesNotContain("docs/2026.0.6-feedback-release.md", readme);
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
