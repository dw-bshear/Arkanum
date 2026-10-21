namespace Arkanum.Tests;

public class PhaseCInstallerTests
{
    [Fact]
    public void LinuxInstallerSupportsUpgradeStateBackupReportAndNativePackageStaging()
    {
        var root = ProjectRoot();
        var installer = Path.Combine(root, "installers", "linux", "install-arkanum.sh");
        var gui = Path.Combine(root, "installers", "linux", "arkanum-gui-install.py");
        var native = Path.Combine(root, "installers", "linux", "build-native-package.sh");
        var guide = Path.Combine(root, "docs", "upgrade-and-native-packaging.md");

        Assert.True(File.Exists(installer), "Linux script installer is missing.");
        Assert.True(File.Exists(gui), "Linux GUI installer is missing.");
        Assert.True(File.Exists(native), "Linux native package staging script is missing.");
        Assert.True(File.Exists(guide), "Upgrade/native packaging guide is missing.");

        var script = File.ReadAllText(installer);
        var guiText = File.ReadAllText(gui);
        var nativeText = File.ReadAllText(native);
        var guideText = File.ReadAllText(guide);

        Assert.Contains("ARKANUM_PACKAGE_VERSION", script);
        Assert.Contains("install-state.json", script);
        Assert.Contains("ACTION=upgrade", script);
        Assert.Contains("ACTION=repair", script);
        Assert.Contains("before-${ARKANUM_PACKAGE_VERSION}", script);
        Assert.Contains("upgrade-report-${ARKANUM_PACKAGE_VERSION}.txt", script);
        Assert.Contains("/health", script);
        Assert.Contains("Rollback app files", script);

        Assert.Contains("Install action", guiText);
        Assert.Contains("upgrade existing installation", guiText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repair current installation", guiText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install side-by-side", guiText, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("dpkg-deb", nativeText);
        Assert.Contains("Arkanum-", nativeText);
        Assert.Contains(".deb", nativeText);

        Assert.Contains("Phase C.1", guideText);
        Assert.Contains("Phase C.2", guideText);
        Assert.Contains("Phase C.3", guideText);
        Assert.Contains("Windows x64 VMware test", guideText);
    }

    [Fact]
    public void WindowsInstallerSupportsUpgradeStateBackupReportAndNativePackageStaging()
    {
        var root = ProjectRoot();
        var installer = Path.Combine(root, "installers", "windows", "Install-Arkanum.ps1");
        var gui = Path.Combine(root, "installers", "windows", "Install-ArkanumGui.ps1");
        var native = Path.Combine(root, "installers", "windows", "Build-ArkanumMsi.ps1");
        var guide = Path.Combine(root, "docs", "upgrade-and-native-packaging.md");

        Assert.True(File.Exists(installer), "Windows script installer is missing.");
        Assert.True(File.Exists(gui), "Windows GUI installer is missing.");
        Assert.True(File.Exists(native), "Windows MSI staging script is missing.");
        Assert.True(File.Exists(guide), "Upgrade/native packaging guide is missing.");

        var script = File.ReadAllText(installer);
        var guiText = File.ReadAllText(gui);
        var nativeText = File.ReadAllText(native);
        var guideText = File.ReadAllText(guide);

        Assert.Contains("PackageVersion", script);
        Assert.Contains("install-state.json", script);
        Assert.Contains("ValidateSet('install','upgrade','repair','side-by-side','restore','auto')", script);
        Assert.Contains("before-$PackageVersion", script);
        Assert.Contains("upgrade-report-$PackageVersion.txt", script);
        Assert.Contains("/health", script);
        Assert.Contains("Rollback app files", script);

        Assert.Contains("Install action", guiText);
        Assert.Contains("Upgrade existing installation", guiText);
        Assert.Contains("Repair current installation", guiText);
        Assert.Contains("Install side-by-side", guiText);

        Assert.Contains("WiX", nativeText);
        Assert.Contains("Arkanum-", nativeText);
        Assert.Contains(".msi", nativeText);

        Assert.Contains("MSI", guideText);
        Assert.Contains("DEB", guideText);
    }

    [Fact]
    public void ProductVersionIsAdvancedForThePhaseCRelease()
    {
        var root = ProjectRoot();
        var props = Path.Combine(root, "Directory.Build.props");
        var releaseGuide = Path.Combine(root, "docs", "upgrade-and-native-packaging.md");

        Assert.True(File.Exists(props), "Directory.Build.props should define the package version for installers and releases.");
        var propsText = File.ReadAllText(props);
        var guideText = File.ReadAllText(releaseGuide);

        Assert.Contains("<Version>1.1.0</Version>", propsText);
        Assert.Contains("1.1.0", guideText);
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
