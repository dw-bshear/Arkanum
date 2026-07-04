using Arkanum.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Arkanum.Tests;

public class Phase2SecurityHardeningTests
{
    [Fact]
    public void SecurityHeadersIncludeBrowserHardeningHeaders()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/";

        SecurityHeadersMiddleware.Apply(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
        Assert.Contains("frame-ancestors 'none'", context.Response.Headers["Content-Security-Policy"].ToString());
        Assert.Contains("camera=()", context.Response.Headers["Permissions-Policy"].ToString());
    }

    [Fact]
    public void SecurityHeadersDisableCachingForVaultAndSettingsPages()
    {
        foreach (var path in new[] { "/Vaults", "/Vaults/Details/00000000-0000-0000-0000-000000000000", "/Settings", "/Audit", "/Users", "/Login" })
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;

            SecurityHeadersMiddleware.Apply(context);

            Assert.Contains("no-store", context.Response.Headers["Cache-Control"].ToString());
            Assert.Equal("no-cache", context.Response.Headers["Pragma"]);
            Assert.Equal("0", context.Response.Headers["Expires"]);
        }
    }

    [Fact]
    public void CookiePolicyUsesHttpOnlyStrictSameSiteAndThirtyMinuteIdleTimeout()
    {
        var options = new CookieAuthenticationOptions();
        var configuration = new ConfigurationBuilder().Build();

        ArkanumCookiePolicy.Configure(options, configuration);

        Assert.Equal("Arkanum.Auth", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, options.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
        Assert.Equal(TimeSpan.FromMinutes(30), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
    }

    [Fact]
    public void CookiePolicyCanBeForcedToSecureAlwaysForHttpsDeployments()
    {
        var options = new CookieAuthenticationOptions();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Arkanum:CookieSecurePolicy"] = "Always" })
            .Build();

        ArkanumCookiePolicy.Configure(options, configuration);

        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void ProductionErrorPageDoesNotIncludeDevelopmentModeInstructions()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Error.cshtml"));

        Assert.Contains("Reference ID", markup);
        Assert.Contains("No secret values", markup);
        Assert.DoesNotContain("Development Mode", markup);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", markup);
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
