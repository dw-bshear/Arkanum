using System.Text.RegularExpressions;

namespace Arkanum.Tests;

public sealed class Issue015DefaultDenyRoutingTests
{
    [Fact]
    public void RazorPagesAreAuthenticatedByDefaultWithExplicitAnonymousExceptions()
    {
        var program = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Program.cs"));

        Assert.Contains("AddRazorPages(options =>", program);
        Assert.Contains("options.Conventions.AuthorizeFolder(\"/\")", program);

        foreach (var page in new[] { "/Index", "/Login", "/ManualLogin", "/Setup", "/Error" })
        {
            Assert.Contains($"options.Conventions.AllowAnonymousToPage(\"{page}\")", program);
        }
    }

    [Fact]
    public void SensitivePagesAreNotAnonymousExceptions()
    {
        var program = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Program.cs"));
        var anonymousExceptions = Regex.Matches(program, "AllowAnonymousToPage\\(\\\"(?<page>[^\\\"]+)\\\"\\)")
            .Select(match => match.Groups["page"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var page in new[] { "/Vaults", "/Vaults/Index", "/Vaults/Details", "/Settings", "/Audit", "/Users", "/Logout" })
        {
            Assert.DoesNotContain(page, anonymousExceptions);
        }
    }

    [Fact]
    public void RazorPagesMappingStillRequiresAuthorizationAsRouteLevelBackstop()
    {
        var program = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Program.cs"));

        Assert.Contains("app.MapRazorPages().WithStaticAssets().RequireAuthorization();", NormalizeWhitespace(program));
    }

    private static string NormalizeWhitespace(string value) => Regex.Replace(value, "\\s+", string.Empty);

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arkanum.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate Arkanum.sln.");
    }
}
