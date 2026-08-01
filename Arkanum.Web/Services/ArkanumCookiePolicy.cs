using Microsoft.AspNetCore.Authentication.Cookies;

namespace Arkanum.Web.Services;

public static class ArkanumCookiePolicy
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(30);

    public static void Configure(CookieAuthenticationOptions options, IConfiguration configuration, string environmentName = "Production")
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/Login";
        options.Cookie.Name = "Arkanum.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = ResolveSecurePolicy(configuration["Arkanum:CookieSecurePolicy"], environmentName);
        options.ExpireTimeSpan = DefaultIdleTimeout;
        options.SlidingExpiration = true;
    }

    public static CookieSecurePolicy ResolveSecurePolicy(string? configured, string environmentName = "Production")
    {
        var allowDowngrade = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
        return configured?.Trim().ToLowerInvariant() switch
        {
            "always" => CookieSecurePolicy.Always,
            "none" when allowDowngrade => CookieSecurePolicy.None,
            "sameasrequest" when allowDowngrade => CookieSecurePolicy.SameAsRequest,
            _ => CookieSecurePolicy.Always
        };
    }
}
