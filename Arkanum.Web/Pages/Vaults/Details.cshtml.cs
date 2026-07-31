using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Arkanum.Web.Pages.Vaults;

public class DetailsModel(VaultRepository repository, VaultItemService itemService, VaultExportService exportService, Arkanum.Web.Data.ArkanumDbContext db) : PageModel
{
    public VaultDetails? Vault { get; private set; }
    public string? RevealedSecret { get; private set; }
    public string? RevealedTitle { get; private set; }
    public Guid? RevealedItemId { get; private set; }
    public bool CopyIntent { get; private set; }
    public bool CanInviteUsers { get; private set; }
    public bool CanEditVaultMetadata { get; private set; }
    public bool CanAddOrChangeVaultContents { get; private set; }
    public bool CanRevealSecrets { get; private set; }
    public bool IsReadOnlyAuditorView { get; private set; }
    public string? InviteStatus { get; private set; }
    public string? FolderStatus { get; private set; }

    [BindProperty] public Guid FolderId { get; set; }
    [BindProperty] public string? Title { get; set; }
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? SecretValue { get; set; }
    [BindProperty] public Guid ItemId { get; set; }
    [BindProperty] public DateOnly? ResetNotificationDate { get; set; }
    [BindProperty] public string? ExportPassphrase { get; set; }
    [BindProperty] public string? InviteUsername { get; set; }
    [BindProperty] public string? FolderName { get; set; }
    [BindProperty] public string? VaultName { get; set; }
    [BindProperty] public string? VaultDescription { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? revealedItemId = null, string? revealedSecret = null, string? revealedTitle = null, bool copyIntent = false)
    {
        RevealedItemId = revealedItemId;
        RevealedSecret = revealedSecret ?? TempData["RevealedSecret"] as string;
        RevealedTitle = revealedTitle ?? TempData["RevealedTitle"] as string;
        CopyIntent = copyIntent || bool.TryParse(TempData["CopyIntent"] as string, out var tempCopyIntent) && tempCopyIntent;
        return await LoadPageAsync(id);
    }

    public async Task<IActionResult> OnPostUpdateMetadataAsync(Guid id)
    {
        if (!await repository.CanEditVaultMetadataAsync(db, id, ActorKey(), CurrentRoles())) return Forbid();
        if (string.IsNullOrWhiteSpace(VaultName)) ModelState.AddModelError(nameof(VaultName), "Vault name is required.");
        if (!ModelState.IsValid) return await LoadPageAsync(id);
        await repository.UpdateVaultMetadataAsync(db, id, VaultName ?? string.Empty, VaultDescription ?? string.Empty, ActorKey(), CurrentRoles(), DateTimeOffset.UtcNow);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCreateFolderAsync(Guid id)
    {
        if (!await repository.CanAddOrChangeVaultContentsAsync(db, id, ActorKey(), CurrentRoles())) return Forbid();
        if (string.IsNullOrWhiteSpace(FolderName)) ModelState.AddModelError(nameof(FolderName), "Folder name is required.");
        if (!ModelState.IsValid) return await LoadPageAsync(id);
        try
        {
            await repository.CreateFolderAsync(db, id, FolderName ?? string.Empty, ActorKey(), DateTimeOffset.UtcNow);
            FolderStatus = $"Folder '{FolderName?.Trim()}' was created.";
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(FolderName), ex.Message);
            return await LoadPageAsync(id);
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostInviteAsync(Guid id)
    {
        if (!await repository.CanInviteAsync(db, id, ActorKey(), CurrentRoles())) return Forbid();
        if (string.IsNullOrWhiteSpace(InviteUsername)) ModelState.AddModelError(nameof(InviteUsername), "Username is required.");
        if (!ModelState.IsValid) return await LoadPageAsync(id);
        try
        {
            await repository.InviteUserAsync(db, id, InviteUsername ?? string.Empty, ActorKey(), DateTimeOffset.UtcNow);
            InviteStatus = $"{InviteUsername?.Trim()} can now access this vault.";
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(InviteUsername), ex.Message);
        }
        return await LoadPageAsync(id);
    }

    public async Task<IActionResult> OnPostCreateLoginAsync(Guid id)
    {
        if (!await repository.CanAddOrChangeVaultContentsAsync(db, id, ActorKey(), CurrentRoles())) return Forbid();
        if (string.IsNullOrWhiteSpace(Title)) ModelState.AddModelError(nameof(Title), "Title is required.");
        if (string.IsNullOrWhiteSpace(SecretValue)) ModelState.AddModelError(nameof(SecretValue), "Password is required.");
        if (!ModelState.IsValid) return await LoadPageAsync(id);
        try
        {
            await itemService.CreateLoginCredentialAsync(db, id, FolderId, Title ?? string.Empty, Username ?? string.Empty, SecretValue ?? string.Empty, ActorKey(), DateTimeOffset.UtcNow);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCreateNoteAsync(Guid id)
    {
        if (!await repository.CanAddOrChangeVaultContentsAsync(db, id, ActorKey(), CurrentRoles())) return Forbid();
        if (string.IsNullOrWhiteSpace(Title)) ModelState.AddModelError(nameof(Title), "Title is required.");
        if (string.IsNullOrWhiteSpace(SecretValue)) ModelState.AddModelError(nameof(SecretValue), "Note is required.");
        if (!ModelState.IsValid) return await LoadPageAsync(id);
        try
        {
            await itemService.CreateSecureNoteAsync(db, id, FolderId, Title ?? string.Empty, SecretValue ?? string.Empty, ActorKey(), DateTimeOffset.UtcNow);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRevealAsync(Guid id)
    {
        if (!await repository.CanRevealSecretAsync(db, id, ActorKey(), CurrentRoles())) return Forbid();
        string secret;
        try
        {
            secret = await itemService.RevealSecretAsync(db, id, ItemId, ActorKey(), SecretAccessPurpose.Reveal, DateTimeOffset.UtcNow);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        TempData["RevealedSecret"] = secret;
        TempData["RevealedTitle"] = Title;
        TempData["CopyIntent"] = "false";
        return RedirectToPage(null, null, new { id, revealedItemId = ItemId });
    }

    public async Task<IActionResult> OnPostCopyAsync(Guid id)
    {
        if (!await repository.CanRevealSecretAsync(db, id, ActorKey(), CurrentRoles())) return Forbid();
        string secret;
        try
        {
            secret = await itemService.RevealSecretAsync(db, id, ItemId, ActorKey(), SecretAccessPurpose.Copy, DateTimeOffset.UtcNow);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        TempData["RevealedSecret"] = secret;
        TempData["RevealedTitle"] = Title;
        TempData["CopyIntent"] = "true";
        return RedirectToPage(null, null, new { id, revealedItemId = ItemId });
    }

    public async Task<IActionResult> OnPostUpdateResetNotificationAsync(Guid id)
    {
        if (!await repository.CanAddOrChangeVaultContentsAsync(db, id, ActorKey(), CurrentRoles())) return Forbid();
        try
        {
            await itemService.UpdateResetNotificationAsync(db, id, ItemId, ResetNotificationDate, ActorKey(), DateTimeOffset.UtcNow);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostExportVaultCsvAsync(Guid id)
    {
        if (!await repository.CanRevealSecretAsync(db, id, ActorKey(), CurrentRoles())) return Forbid();
        if (string.IsNullOrWhiteSpace(ExportPassphrase)) ModelState.AddModelError(nameof(ExportPassphrase), "Export passphrase is required.");
        if (!ModelState.IsValid) return await LoadPageAsync(id);
        var export = await exportService.ExportVaultCsvZipAsync(db, id, ActorKey(), ExportPassphrase ?? string.Empty, DateTimeOffset.UtcNow);
        return File(export.Content, export.ContentType, export.FileName);
    }

    private async Task<IActionResult> LoadPageAsync(Guid id)
    {
        var roles = CurrentRoles();
        if (!await repository.CanAccessVaultAsync(db, id, ActorKey(), roles)) return Forbid();
        Vault = await repository.GetVaultDetailsAsync(db, id);
        CanInviteUsers = await repository.CanInviteAsync(db, id, ActorKey(), roles);
        CanEditVaultMetadata = await repository.CanEditVaultMetadataAsync(db, id, ActorKey(), roles);
        CanAddOrChangeVaultContents = await repository.CanAddOrChangeVaultContentsAsync(db, id, ActorKey(), roles);
        CanRevealSecrets = await repository.CanRevealSecretAsync(db, id, ActorKey(), roles);
        IsReadOnlyAuditorView = ArkanumRoleCapabilities.IsAuditorOnly(roles);
        VaultName = Vault?.Name;
        VaultDescription = Vault?.Description;
        return Vault is null ? NotFound() : Page();
    }

    private HashSet<ArkanumRole> CurrentRoles()
    {
        if (User?.Identity?.IsAuthenticated != true) return [];

        var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role)
            .Select(c => ArkanumRoleCatalog.ParseClaimRole(c.Value))
            .OfType<ArkanumRole>()
            .ToHashSet();
        return roles;
    }

    private string ActorKey() => User?.Identity?.IsAuthenticated == true
        ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException("Authenticated principal is missing a stable user key.")
        : throw new UnauthorizedAccessException("Authenticated principal is required.");
}
