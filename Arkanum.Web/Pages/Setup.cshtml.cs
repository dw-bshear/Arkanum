using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkanum.Web.Pages;

public class SetupModel(ArkanumDbContext db, FirstRunSetupService setupService, LocalPasswordHasher passwordHasher, IConfiguration configuration, SiteRecoveryKeyService recoveryKeys) : PageModel
{
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public string ConfirmPassword { get; set; } = string.Empty;
    public bool SetupRequired { get; private set; }
    public string? GeneratedRecoveryKey { get; private set; }
    public bool SetupCompleted { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        SetupRequired = setupService.IsSetupRequired(await setupService.LoadUsersAsync(db));
        return SetupRequired ? Page() : RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        SetupRequired = setupService.IsSetupRequired(await setupService.LoadUsersAsync(db));
        if (!SetupRequired) return RedirectToPage("/Index");
        if (string.IsNullOrWhiteSpace(Username)) ModelState.AddModelError(nameof(Username), "Username is required.");
        if (Password != ConfirmPassword) ModelState.AddModelError(nameof(ConfirmPassword), "Passwords must match.");
        if (Password.Length < 12) ModelState.AddModelError(nameof(Password), "Password must be at least 12 characters long.");
        if (!ModelState.IsValid) return Page();

        GeneratedRecoveryKey = EnvironmentMasterKeyProvider.EnsureLocalMasterKey(configuration);
        await setupService.CreateInitialAdministratorAsync(db, passwordHasher, Username, Password, DateTimeOffset.UtcNow);
        await recoveryKeys.EnsureRecoverySentinelAsync(db, DateTimeOffset.UtcNow);
        SetupCompleted = true;
        SetupRequired = false;
        TempData["Status"] = "Initial administrator created. Save the Arkanum Recovery Key before signing in.";
        return Page();
    }
}
