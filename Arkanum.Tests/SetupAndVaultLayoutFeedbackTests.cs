using System.Text.RegularExpressions;

namespace Arkanum.Tests;

public class SetupAndVaultLayoutFeedbackTests
{
    [Fact]
    public void SetupRecoveryStepUsesFullWidthAndSpacedControls()
    {
        var css = Read("wwwroot", "css", "site.css");
        var formRule = Regex.Match(css, @"\.setup-form\s*\{([^}]+)\}").Groups[1].Value;
        Assert.Contains("max-width: none", formRule);
        Assert.Contains("width: 100%", formRule);
        var stepRule = Regex.Match(css, @"\.setup-recovery-key-step\s*\{([^}]+)\}").Groups[1].Value;
        Assert.Contains("display: grid", stepRule);
        Assert.Contains("gap:", stepRule);
    }

    [Fact]
    public void SetupUsesSimpleRecoveryStorageCopyWithoutRouteLockWording()
    {
        var markup = Read("Pages", "Setup.cshtml");
        Assert.DoesNotContain("The route locks after an administrator exists", markup);
        Assert.Contains("Store the recovery key somewhere safe.", markup);
        Assert.DoesNotContain("Store the recovery key offline with the database backup materials", markup);
    }

    [Fact]
    public void VaultAccessFollowsBothEntryForms()
    {
        var markup = Read("Pages", "Vaults", "Details.cshtml");
        var contents = markup.IndexOf("id=\"vault-contents\"", StringComparison.Ordinal);
        var credential = markup.IndexOf("id=\"vault-add-entry\"", StringComparison.Ordinal);
        var note = markup.IndexOf("id=\"vault-add-note\"", StringComparison.Ordinal);
        var access = markup.IndexOf("id=\"vault-access\"", StringComparison.Ordinal);
        Assert.True(contents >= 0 && contents < credential && credential < note && note < access);
        Assert.Contains("class=\"feature-card access-card vault-section-card vault-section-access\"", markup);
    }

    [Fact]
    public void VaultSectionNavigationMatchesContentOrderForEditableAndReadOnlyViews()
    {
        var markup = Read("Pages", "Vaults", "Details.cshtml");
        var nav = Regex.Match(markup, @"<nav class=""vault-section-nav""[\s\S]*?</nav>").Value;
        Assert.Contains("href=\"#vault-contents\"><span>1</span>", nav);
        Assert.Contains("href=\"#vault-add-entry\"><span>2</span>", nav);
        Assert.Contains("href=\"#vault-add-note\"><span>3</span>", nav);
        Assert.Contains("@(Model.CanAddOrChangeVaultContents ? 4 : 2)", nav);
        Assert.True(nav.IndexOf("#vault-access", StringComparison.Ordinal) > nav.IndexOf("#vault-add-note", StringComparison.Ordinal));
    }

    private static string Read(params string[] path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln"))) dir = dir.Parent;
        var root = dir?.FullName ?? throw new InvalidOperationException("Could not find project root.");
        return File.ReadAllText(Path.Combine(new[] { root, "Arkanum.Web" }.Concat(path).ToArray()));
    }
}
