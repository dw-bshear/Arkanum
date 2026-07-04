using System.IO;
using Xunit;

namespace Arkanum.Tests;

public class PortalCopyTests
{
    [Fact]
    public void LayoutFooterAndDashboardUseRequestedVaultCopy()
    {
        var root = ProjectRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Shared", "_Layout.cshtml"));
        var dashboard = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Index.cshtml"));

        Assert.Contains("Arkanum credential vaults", layout);
        Assert.Contains("Access History · Secure Notes · Safe Credentials", layout);
        Assert.DoesNotContain("Arkanum internal credential vault", layout);
        Assert.DoesNotContain("Access History · Secret Access Events · Security Review", layout);

        Assert.Contains("Administrative and role-specific tools are grouped under Settings.", dashboard);
        Assert.DoesNotContain("Administrative and role-specific tools are grouped under Settings so the dashboard stays calm.", dashboard);
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
