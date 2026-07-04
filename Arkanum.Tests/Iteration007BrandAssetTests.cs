using System.IO;
using Xunit;

namespace Arkanum.Tests;

public class Iteration007BrandAssetTests
{
    [Fact]
    public void Shared_layout_uses_real_brand_assets()
    {
        var root = ProjectRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Shared", "_Layout.cshtml"));

        Assert.Contains("~/favicon.ico", layout);
        Assert.Contains("~/favicon-32x32.png", layout);
        Assert.Contains("~/images/apple-touch-icon.png", layout);
        Assert.Contains("brand-image", layout);
        Assert.Contains("~/images/brand/header-mark.webp", layout);
        Assert.DoesNotContain(">A</span>Arkanum", layout);
    }

    [Fact]
    public void Login_and_landing_pages_use_brand_background_class()
    {
        var root = ProjectRoot();
        var login = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Login.cshtml"));
        var index = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Index.cshtml"));
        var css = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "wwwroot", "css", "site.css"));

        Assert.Contains("brand-background-page", login);
        Assert.Contains("brand-background-page", index);
        Assert.Contains("/images/brand/login-background.webp", css);
        Assert.Contains("/images/brand/login-background-mobile.webp", css);
    }

    [Fact]
    public void Generated_brand_assets_exist()
    {
        var webRoot = Path.Combine(ProjectRoot(), "Arkanum.Web", "wwwroot");
        foreach (var relative in new[]
        {
            "favicon.ico",
            "favicon-32x32.png",
            Path.Combine("images", "apple-touch-icon.png"),
            Path.Combine("images", "brand", "header-mark.webp"),
            Path.Combine("images", "brand", "login-background.webp"),
            Path.Combine("images", "brand", "login-background-mobile.webp"),
            Path.Combine("images", "brand", "BRAND_ASSETS.json")
        })
        {
            var path = Path.Combine(webRoot, relative);
            Assert.True(File.Exists(path), $"Missing generated brand asset: {relative}");
            Assert.True(new FileInfo(path).Length > 0, $"Generated brand asset is empty: {relative}");
        }
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
