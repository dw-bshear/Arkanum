using System.IO;

namespace Arkanum.Tests;

public class Issue010PreAuthDisclosureTests
{
    [Fact]
    public void Landing_page_has_low_disclosure_guest_branch_without_vault_or_settings_shortcuts()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Index.cshtml"));

        var guestBranch = markup[..markup.IndexOf("else", StringComparison.Ordinal)];

        Assert.Contains("Model.IsAuthenticated", markup);
        Assert.Contains("pre-auth-sign-in", guestBranch);
        Assert.Contains("Sign in to continue", guestBranch);
        Assert.Contains("!Model.IsAuthenticated", markup);
        Assert.DoesNotContain("asp-page=\"/Vaults/Index\"", guestBranch);
        Assert.DoesNotContain("Access events", guestBranch);
        Assert.DoesNotContain("asp-page=\"/Settings/Index\"", guestBranch);
    }

    [Fact]
    public void Landing_page_model_only_queries_counts_after_authentication()
    {
        var model = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Index.cshtml.cs"));

        Assert.Contains("public bool IsAuthenticated", model);
        Assert.Contains("if (!IsAuthenticated)", model);
        Assert.True(model.IndexOf("if (!IsAuthenticated)", StringComparison.Ordinal) < model.IndexOf("VaultCount = await db.Vaults.CountAsync()", StringComparison.Ordinal));
        Assert.True(model.IndexOf("if (!IsAuthenticated)", StringComparison.Ordinal) < model.IndexOf("RecentAuditCount = await db.AuditEvents.CountAsync()", StringComparison.Ordinal));
    }

    [Fact]
    public void Shared_layout_hides_dashboard_navigation_from_guests()
    {
        var layout = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Shared", "_Layout.cshtml"));

        Assert.Contains("User?.Identity?.IsAuthenticated == true", layout);
        Assert.DoesNotContain("<li class=\"nav-item\"><a class=\"nav-link\" asp-page=\"/Index\">Dashboard</a></li>\n                        @if (User?.Identity?.IsAuthenticated == true)", layout);
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
