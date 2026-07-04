using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkanum.Web.Pages;

public class ManualLoginModel(ArkanumDbContext db, LocalAuthenticationService authService, FirstRunSetupService setupService) : PageModel
{
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public string StatusMessage { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        if (setupService.IsSetupRequired(await setupService.LoadUsersAsync(db))) return RedirectToPage("/Setup");
        StatusMessage = TempData["Status"] as string ?? string.Empty;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (setupService.IsSetupRequired(await setupService.LoadUsersAsync(db))) return RedirectToPage("/Setup");
        var result = await authService.AuthenticateAsync(db, Username, Password, DateTimeOffset.UtcNow);
        if (!result.Succeeded || result.User is null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, FederatedAuthenticationService.BuildApplicationPrincipal(result.User));
        return RedirectToPage("/Index");
    }
}
