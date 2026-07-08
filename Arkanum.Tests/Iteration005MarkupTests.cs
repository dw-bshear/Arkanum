using System.IO;
using Xunit;

namespace Arkanum.Tests;

public class Iteration005MarkupTests
{
    [Fact]
    public void Vault_list_keeps_create_vault_as_disclosure_and_removes_password_generator_card()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Vaults", "Index.cshtml"));

        Assert.Contains("<details class=\"create-vault-disclosure\">", markup);
        Assert.Contains("<summary class=\"secondary-action\">Create vault</summary>", markup);
        Assert.DoesNotContain("Password generator", markup);
        Assert.DoesNotContain("data-demo-password", markup);
    }

    [Fact]
    public void Credential_form_contains_password_generator_next_to_password_entry()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Vaults", "Details.cshtml"));

        Assert.Contains("add-credential-form", markup);
        Assert.Contains("add-note-form", markup);
        Assert.Contains("id=\"credentialPassword\"", markup);
        Assert.Contains("data-generate-password", markup);
        Assert.Contains("data-password-target=\"credentialPassword\"", markup);
        Assert.Contains("Need a strong password?", markup);
    }

    [Fact]
    public void Vault_detail_combines_access_invite_and_renders_folder_tree()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Vaults", "Details.cshtml"));

        Assert.Contains("access-card", markup);
        Assert.Contains("vault-section-nav", markup);
        Assert.Contains("href=\"#vault-contents\"", markup);
        Assert.Contains("class=\"invite-user-disclosure\"", markup);
        Assert.Contains("<summary class=\"secondary-action small\">Invite user</summary>", markup);
        Assert.Contains("class=\"vault-tree\"", markup);
        Assert.Contains("class=\"tree-folder\"", markup);
        Assert.DoesNotContain("class=\"tree-folder\" open", markup);
        Assert.Contains("class=\"tree-items\"", markup);
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
