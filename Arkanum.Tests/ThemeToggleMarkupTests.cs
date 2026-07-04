using System.IO;
using Xunit;

namespace Arkanum.Tests;

public class ThemeToggleMarkupTests
{
    [Fact]
    public void LayoutUsesTwoStateSunMoonThemeToggleWithoutSystemOption()
    {
        var root = ProjectRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Shared", "_Layout.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "wwwroot", "js", "site.js"));
        var css = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "wwwroot", "css", "site.css"));

        Assert.Contains("theme-switch", layout);
        Assert.Contains("data-theme-toggle", layout);
        Assert.Contains("☀", layout);
        Assert.Contains("☾", layout);
        Assert.Contains("data-theme-choice=\"light\"", layout);
        Assert.Contains("data-theme-choice=\"dark\"", layout);
        Assert.DoesNotContain("data-theme-choice=\"system\"", layout);
        Assert.DoesNotContain(">System<", layout);
        Assert.DoesNotContain(">Light<", layout);
        Assert.DoesNotContain(">Dark<", layout);

        Assert.Contains("data-theme-toggle", script);
        Assert.DoesNotContain("prefers-color-scheme", script);
        Assert.DoesNotContain("system", script);
        Assert.Contains("theme-switch", css);
        Assert.Contains("theme-switch-thumb", css);
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
