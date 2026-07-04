using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkanum.Web.Pages;

public class LoginModel(ArkanumDbContext db, FirstRunSetupService setupService, FederatedAuthenticationService federatedAuth) : PageModel
{
    public string StatusMessage { get; private set; } = string.Empty;
    public string FederationErrorMessage { get; private set; } = string.Empty;
    public FederationSettings Federation { get; private set; } = new("Local", string.Empty, string.Empty, false, "openid profile email", string.Empty, string.Empty, true);
    public bool OidcSignInAvailable => Federation.OidcConfigured;

    public async Task<IActionResult> OnGetAsync(bool federationError = false)
    {
        if (setupService.IsSetupRequired(await setupService.LoadUsersAsync(db))) return RedirectToPage("/Setup");
        await LoadFederationAsync();
        StatusMessage = TempData["Status"] as string ?? string.Empty;
        FederationErrorMessage = federationError ? "Federated sign-in did not complete. Use manual sign in for emergency local access or contact an administrator." : string.Empty;
        return Page();
    }

    public async Task<IActionResult> OnPostOidcAsync()
    {
        if (setupService.IsSetupRequired(await setupService.LoadUsersAsync(db))) return RedirectToPage("/Setup");
        await LoadFederationAsync();
        if (!OidcSignInAvailable)
        {
            ModelState.AddModelError(string.Empty, "OIDC sign-in is not configured. Use manual sign in or have an administrator finish the SAML/OIDC settings.");
            return Page();
        }

        return Challenge(new AuthenticationProperties { RedirectUri = Url.Page("/Index") ?? "/" }, FederatedAuthenticationService.OidcScheme);
    }

    private async Task LoadFederationAsync()
    {
        Federation = await federatedAuth.GetFederationAsync(db);
    }
}
