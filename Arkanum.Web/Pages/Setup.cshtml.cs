using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkanum.Web.Pages;

public class SetupModel(ArkanumDbContext db, FirstRunSetupService setupService, LocalPasswordHasher passwordHasher, IConfiguration configuration, SiteRecoveryKeyService recoveryKeys) : PageModel
{
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public string ConfirmPassword { get; set; } = string.Empty;
    [BindProperty] public bool RecoveryKeySavedAcknowledged { get; set; }
    public bool SetupRequired { get; private set; }
    public string? GeneratedRecoveryKey { get; private set; }
    public string? DisplayRecoveryKey { get; private set; }
    public bool SetupCompleted { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        SetupRequired = setupService.IsSetupRequired(await setupService.LoadUsersAsync(db));
        if (!SetupRequired) return RedirectToPage("/Index");
        LoadOrCreateDisplayRecoveryKey();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        SetupRequired = setupService.IsSetupRequired(await setupService.LoadUsersAsync(db));
        if (!SetupRequired) return RedirectToPage("/Index");

        LoadOrCreateDisplayRecoveryKey();
        if (string.IsNullOrWhiteSpace(Username)) ModelState.AddModelError(nameof(Username), "Username is required.");
        if (Password != ConfirmPassword) ModelState.AddModelError(nameof(ConfirmPassword), "Passwords must match.");
        if (Password.Length < 12) ModelState.AddModelError(nameof(Password), "Password must be at least 12 characters long.");
        if (!RecoveryKeySavedAcknowledged) ModelState.AddModelError(nameof(RecoveryKeySavedAcknowledged), "You must acknowledge that the Arkanum Recovery Key has been saved before setup can continue.");
        if (!ModelState.IsValid) return Page();

        await setupService.CreateInitialAdministratorAsync(db, passwordHasher, Username, Password, DateTimeOffset.UtcNow);
        await recoveryKeys.EnsureRecoverySentinelAsync(db, DateTimeOffset.UtcNow);
        SetupCompleted = true;
        SetupRequired = false;
        TempData["Status"] = "Initial administrator created. The Arkanum Recovery Key acknowledgment was recorded for setup.";
        return Page();
    }

    private void LoadOrCreateDisplayRecoveryKey()
    {
        GeneratedRecoveryKey = EnvironmentMasterKeyProvider.EnsureLocalMasterKey(configuration);
        DisplayRecoveryKey = GeneratedRecoveryKey ?? EnvironmentMasterKeyProvider.TryReadConfiguredRecoveryKey(configuration);
    }
}
