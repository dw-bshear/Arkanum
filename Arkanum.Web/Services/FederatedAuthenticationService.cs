using System.Security.Claims;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Arkanum.Web.Services;

public sealed class FederatedAuthenticationService(PortalSettingsService settingsService)
{
    public const string OidcScheme = "ArkanumOidc";

    public async Task<FederationSettings> GetFederationAsync(ArkanumDbContext db)
    {
        var settings = await settingsService.GetAllAsync(db);
        return PortalSettingsService.ToFederation(settings);
    }

    public async Task<bool> IsOidcSignInAvailableAsync(ArkanumDbContext db)
    {
        var federation = await GetFederationAsync(db);
        return federation.OidcConfigured;
    }

    public async Task<ArkanumUser?> ResolveFederatedPrincipalAsync(ArkanumDbContext db, ClaimsPrincipal principal, DateTimeOffset now)
    {
        var externalId = FirstClaim(principal, "sub", ClaimTypes.NameIdentifier);
        var username = FirstClaim(principal, "preferred_username", "upn", ClaimTypes.Upn, ClaimTypes.Email, ClaimTypes.Name);
        var email = FirstClaim(principal, ClaimTypes.Email, "email");
        var lookupUsername = username.Trim();
        var lookupEmail = email.Trim();

        if (string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(lookupUsername) && string.IsNullOrWhiteSpace(lookupEmail))
        {
            db.AuditEvents.Add(new AuditEventEntity
            {
                Type = AuditEventType.LoginFailed,
                ActorUserKey = "federated",
                Target = "external-identity",
                OccurredAt = now,
                Message = "Federated login failed because the assertion did not contain a stable subject, username, or email claim."
            });
            await db.SaveChangesAsync();
            return null;
        }

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Source != UserSource.Local &&
            ((externalId != string.Empty && u.ExternalId == externalId) ||
             (lookupUsername != string.Empty && u.Username == lookupUsername) ||
             (lookupEmail != string.Empty && u.Email == lookupEmail)));

        if (user is null)
        {
            db.AuditEvents.Add(new AuditEventEntity
            {
                Type = AuditEventType.LoginFailed,
                ActorUserKey = "federated",
                Target = lookupUsername == string.Empty ? lookupEmail : lookupUsername,
                OccurredAt = now,
                Message = "Federated login failed because the external identity is not an active Arkanum user. Import or create the user before granting application access."
            });
            await db.SaveChangesAsync();
            return null;
        }

        var domainUser = FirstRunSetupService.ToDomain(user);
        if (!domainUser.CanLogin)
        {
            db.AuditEvents.Add(new AuditEventEntity
            {
                Type = AuditEventType.LoginFailed,
                ActorUserKey = user.UserKey,
                Target = user.Username,
                OccurredAt = now,
                Message = "Federated login failed because the mapped Arkanum user is disabled or not present in the directory."
            });
            await db.SaveChangesAsync();
            return null;
        }

        if (string.IsNullOrWhiteSpace(user.ExternalId) && !string.IsNullOrWhiteSpace(externalId))
        {
            user.ExternalId = externalId;
        }

        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.LoginSucceeded,
            ActorUserKey = user.UserKey,
            Target = user.Username,
            OccurredAt = now,
            Message = "Federated OIDC login succeeded."
        });
        await db.SaveChangesAsync();
        return FirstRunSetupService.ToDomain(user);
    }

    public static ClaimsPrincipal BuildApplicationPrincipal(ArkanumUser user, string authenticationType = CookieAuthenticationDefaults.AuthenticationScheme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserKey),
            new(ClaimTypes.Name, user.Username)
        };
        if (!string.IsNullOrWhiteSpace(user.Email)) claims.Add(new Claim(ClaimTypes.Email, user.Email));
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role.ToString())));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

    private static string FirstClaim(ClaimsPrincipal principal, params string[] claimTypes) =>
        claimTypes.Select(t => principal.FindFirst(t)?.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

public sealed class ArkanumOidcOptionsConfigurator(IServiceScopeFactory scopeFactory, IConfiguration configuration) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(OpenIdConnectOptions options) => Configure(FederatedAuthenticationService.OidcScheme, options);

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (!string.Equals(name, FederatedAuthenticationService.OidcScheme, StringComparison.Ordinal)) return;

        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.SaveTokens = false;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.CallbackPath = "/signin-oidc";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "name",
            RoleClaimType = ClaimTypes.Role
        };

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArkanumDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<PortalSettingsService>();
        var settings = settingsService.GetAllAsync(db).GetAwaiter().GetResult();
        var federation = PortalSettingsService.ToFederation(settings);

        if (federation.OidcConfigured)
        {
            options.Authority = federation.OidcAuthority;
            var allowInsecureMetadata = configuration.GetValue<bool>("Arkanum:Federation:AllowInsecureOidcMetadata");
            options.RequireHttpsMetadata = !(federation.OidcAuthority.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && allowInsecureMetadata);
            options.ClientId = federation.OidcClientId;
            options.ClientSecret = settings.TryGetValue(PortalSettingKeys.OidcClientSecret, out var secret) ? secret : string.Empty;
            options.Scope.Clear();
            foreach (var scopeName in federation.OidcScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                options.Scope.Add(scopeName);
            }
        }
        else
        {
            options.Authority = "https://oidc.not-configured.invalid";
            options.ClientId = "arkanum-not-configured";
        }

        options.Events ??= new OpenIdConnectEvents();
        options.Events.OnTokenValidated = async context =>
        {
            using var eventScope = scopeFactory.CreateScope();
            var eventDb = eventScope.ServiceProvider.GetRequiredService<ArkanumDbContext>();
            var federatedAuth = eventScope.ServiceProvider.GetRequiredService<FederatedAuthenticationService>();
            var user = await federatedAuth.ResolveFederatedPrincipalAsync(eventDb, context.Principal ?? new ClaimsPrincipal(), DateTimeOffset.UtcNow);
            if (user is null)
            {
                context.Fail("Federated identity is not mapped to an active Arkanum user.");
                return;
            }

            context.Principal = FederatedAuthenticationService.BuildApplicationPrincipal(user, FederatedAuthenticationService.OidcScheme);
        };
        options.Events.OnRemoteFailure = context =>
        {
            Console.Error.WriteLine($"Arkanum OIDC remote failure: {context.Failure?.GetType().Name}: {context.Failure?.Message}");
            context.HandleResponse();
            context.Response.Redirect("/Login?federationError=1");
            return Task.CompletedTask;
        };
    }
}
