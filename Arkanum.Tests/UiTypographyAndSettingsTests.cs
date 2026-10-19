using System.Text.RegularExpressions;
using Xunit;

namespace Arkanum.Tests;

public class UiTypographyAndSettingsTests
{
    [Theory]
    [InlineData("normal", "SourceSans3VF-Upright.ttf")]
    [InlineData("italic", "SourceSans3VF-Italic.ttf")]
    public void Site_font_is_bundled_offline_with_full_existing_weight_range(string style, string fileName)
    {
        var css = ReadWeb("wwwroot/css/site.css");
        var faces = Regex.Matches(css, @"@font-face\s*\{([^}]+)\}")
            .Select(match => match.Groups[1].Value);
        Assert.Contains(faces, face =>
            face.Contains("font-family: \"Source Sans 3\";") &&
            face.Contains($"url('../fonts/source-sans-3/{fileName}') format('truetype')") &&
            face.Contains($"font-style: {style};") &&
            face.Contains("font-weight: 200 900;") &&
            face.Contains("font-display: swap;"));
        var body = Regex.Match(css, @"(?m)^body\s*\{([^}]+)\}").Groups[1].Value;
        Assert.Contains("font-family: \"Source Sans 3\", sans-serif;", body);
        Assert.DoesNotContain("@import", css);
        Assert.DoesNotContain("local(", css);
        Assert.DoesNotContain("https://", css);
        var bytes = File.ReadAllBytes(Path.Combine(ProjectRoot(), "Arkanum.Web", "wwwroot", "fonts", "source-sans-3", fileName));
        Assert.True(bytes.Length > 10000, "Bundle the actual font, not a download error or placeholder.");
        Assert.Equal(new byte[] { 0, 1, 0, 0 }, bytes[..4]);
        Assert.Contains("fvar", System.Text.Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    public void Bundled_font_includes_original_redistribution_license()
    {
        var license = ReadWeb("wwwroot/fonts/source-sans-3/LICENSE.md");
        Assert.Contains("Adobe", license);
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", license);
        Assert.Contains("Permission is hereby granted", license);
    }

    [Fact]
    public void Font_change_keeps_existing_typography_formatting()
    {
        var css = ReadWeb("wwwroot/css/site.css");
        Assert.Contains("h1 { font-size: clamp(2rem, 5vw, 4.2rem); line-height: .95; font-weight: 850; letter-spacing: -.06em; margin-bottom: 1rem; }", css);
        Assert.Contains("h2 { font-weight: 800; font-size: 1.15rem; }", css);
        Assert.Contains("letter-spacing: .12em; font-weight: 850; font-size: .78rem;", css);
        Assert.Contains(".brand { display: flex; align-items: center; gap: .6rem; font-weight: 800; letter-spacing: -.02em; }", css);
    }

    [Fact]
    public void Settings_omits_layout_explainer_but_keeps_permission_view_tabs_and_forms()
    {
        var markup = ReadWeb("Pages/Settings/Index.cshtml");
        Assert.DoesNotContain("Settings layout", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tabs separate dashboard copy", markup);
        Assert.Contains("Your permission view", markup);
        Assert.Contains("@foreach (var role in Model.CurrentRoles)", markup);
        Assert.Contains("data-preserve-settings-tab data-preserve-scroll", markup);
        foreach (var tab in new[] { "dashboard", "directory", "database", "federation", "notifications", "users", "logs" })
        {
            Assert.Contains($"data-settings-tab=\"settings-{tab}\"", markup);
            Assert.Contains($"id=\"settings-{tab}\"", markup);
        }
        foreach (var handler in new[] { "DashboardText", "Ldap", "RunDirectorySync", "LdapSyncSchedule", "SqlServer", "Federation", "Mail", "NotificationTemplates", "SendAllUsersNotification", "ToggleUserAccess", "UpdateUserRoles", "Syslog" })
            Assert.Contains($"asp-page-handler=\"{handler}\"", markup);
    }

    private static string ReadWeb(string path) => File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", path));

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find project root.");
    }
}
