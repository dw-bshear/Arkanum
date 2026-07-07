using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Arkanum.Web.Pages.Settings;

public class IndexModel(ArkanumDbContext db, DashboardService dashboardService, PortalSettingsService portalSettings, AdminUserService adminUserService, DirectoryLdapImportService directoryImportService, VaultExportService exportService) : PageModel
{
    public IReadOnlyList<DashboardWidgetEntity> Widgets { get; private set; } = [];
    public IReadOnlyList<SettingsFeatureLink> AvailableFeatures { get; private set; } = [];
    public IReadOnlyCollection<ArkanumRole> CurrentRoles { get; private set; } = [];
    public IReadOnlyList<ArkanumRoleDescription> RoleDescriptions => ArkanumRoleCatalog.Descriptions;
    public bool CanManageFeatureVisibility => CurrentRoles.Contains(ArkanumRole.Administrator);
    public DashboardTextSettings DashboardText { get; private set; } = new(string.Empty, string.Empty);
    public LdapSettings Ldap { get; private set; } = new(string.Empty, "389", string.Empty, string.Empty, string.Empty, "(objectClass=person)", "uid", "displayName", "mail");
    public LdapSyncSettings LdapSync { get; private set; } = new("12h");
    public MailServerSettings Mail { get; private set; } = new(string.Empty, "587", true, string.Empty, "Arkanum");
    public NotificationTemplateSettings NotificationTemplates { get; private set; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
    public SyslogSettings Syslog { get; private set; } = new(string.Empty, "514", "UDP", false);
    public SqlServerSettings SqlServer { get; private set; } = new(string.Empty, "1433", "Arkanum", string.Empty, false, true, true);
    public FederationSettings Federation { get; private set; } = new("Local", string.Empty, string.Empty, false, "openid profile email", string.Empty, string.Empty, true);
    public string ActiveDatabaseProvider { get; private set; } = string.Empty;
    public IReadOnlyList<AppUserEntity> ManagedUsers { get; private set; } = [];
    public IReadOnlyList<UserGroupEntity> Groups { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> GroupMemberNames { get; private set; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, string> GroupManagerNames { get; private set; } = new Dictionary<Guid, string>();
    public string UserSearch { get; private set; } = string.Empty;
    public int ActiveUserCount { get; private set; }
    public int DirectoryUserCount { get; private set; }
    public int LocalUserCount { get; private set; }

    [BindProperty] public string? WidgetKey { get; set; }
    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public string? Title { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public string? RolesCsv { get; set; }
    [BindProperty] public int SortOrder { get; set; }

    [BindProperty] public string? DashboardHeroTitle { get; set; }
    [BindProperty] public string? DashboardHeroDescription { get; set; }

    [BindProperty] public string? LdapHost { get; set; }
    [BindProperty] public string? LdapPort { get; set; }
    [BindProperty] public string? LdapBaseDn { get; set; }
    [BindProperty] public string? LdapBindDn { get; set; }
    [BindProperty] public string? LdapBindPassword { get; set; }
    [BindProperty] public string? LdapUserFilter { get; set; }
    [BindProperty] public string? LdapLoginAttribute { get; set; }
    [BindProperty] public string? LdapDisplayNameAttribute { get; set; }
    [BindProperty] public string? LdapEmailAttribute { get; set; }
    [BindProperty] public string? LdapSyncInterval { get; set; }

    [BindProperty] public string? MailSmtpHost { get; set; }
    [BindProperty] public string? MailSmtpPort { get; set; }
    [BindProperty] public bool MailUseTls { get; set; }
    [BindProperty] public string? MailFromAddress { get; set; }
    [BindProperty] public string? MailFromName { get; set; }
    [BindProperty] public string? NotificationAllUsersSubject { get; set; }
    [BindProperty] public string? NotificationAllUsersBody { get; set; }
    [BindProperty] public string? NotificationVaultAccessSubject { get; set; }
    [BindProperty] public string? NotificationVaultAccessBody { get; set; }

    [BindProperty] public string? SyslogHost { get; set; }
    [BindProperty] public string? SyslogPort { get; set; }
    [BindProperty] public string? SyslogProtocol { get; set; }
    [BindProperty] public bool SyslogEnabled { get; set; }

    [BindProperty] public string? SqlServerHost { get; set; }
    [BindProperty] public string? SqlServerPort { get; set; }
    [BindProperty] public string? SqlServerDatabase { get; set; }
    [BindProperty] public string? SqlServerUser { get; set; }
    [BindProperty] public string? SqlServerPassword { get; set; }
    [BindProperty] public bool SqlServerEncrypt { get; set; }
    [BindProperty] public bool SqlServerTrustServerCertificate { get; set; }
    [BindProperty] public string? FederationMode { get; set; }
    [BindProperty] public string? OidcAuthority { get; set; }
    [BindProperty] public string? OidcClientId { get; set; }
    [BindProperty] public string? OidcClientSecret { get; set; }
    [BindProperty] public string? OidcScopes { get; set; }
    [BindProperty] public string? SamlMetadataUrl { get; set; }
    [BindProperty] public string? SamlEntityId { get; set; }
    [BindProperty] public bool RequireExternalMfa { get; set; }

    [BindProperty] public string? CreateUsername { get; set; }
    [BindProperty] public string? CreateDisplayName { get; set; }
    [BindProperty] public string? CreateEmail { get; set; }
    [BindProperty] public string? CreatePassword { get; set; }
    [BindProperty] public List<ArkanumRole> CreateRoles { get; set; } = [ArkanumRole.User];
    [BindProperty] public string? UserKey { get; set; }
    [BindProperty] public bool UserAccessEnabled { get; set; }
    [BindProperty] public List<ArkanumRole> UserRoles { get; set; } = [];
    [BindProperty] public string? GroupName { get; set; }
    [BindProperty] public string? GroupDescription { get; set; }
    [BindProperty] public Guid GroupId { get; set; }
    [BindProperty] public string? GroupUserKey { get; set; }
    [BindProperty] public bool GroupDelegatedManager { get; set; }
    [BindProperty] public string? ExportAllVaultsPassphrase { get; set; }

    public async Task OnGetAsync(string? userSearch = null) => await LoadAsync(userSearch);

    public async Task<IActionResult> OnPostDashboardTextAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        if (string.IsNullOrWhiteSpace(DashboardHeroTitle)) ModelState.AddModelError(nameof(DashboardHeroTitle), "Dashboard headline is required.");
        if (string.IsNullOrWhiteSpace(DashboardHeroDescription)) ModelState.AddModelError(nameof(DashboardHeroDescription), "Dashboard comments are required.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        await portalSettings.SaveDashboardTextAsync(db, DashboardHeroTitle ?? string.Empty, DashboardHeroDescription ?? string.Empty, ActorKey());
        TempData["Status"] = "Dashboard text saved.";
        return RedirectToPage(null, null, "dashboard");
    }

    public async Task<IActionResult> OnPostLdapAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        await portalSettings.SaveLdapAsync(db, new LdapSettings(LdapHost ?? string.Empty, LdapPort ?? string.Empty, LdapBaseDn ?? string.Empty, LdapBindDn ?? string.Empty, LdapBindPassword ?? string.Empty, LdapUserFilter ?? string.Empty, LdapLoginAttribute ?? string.Empty, LdapDisplayNameAttribute ?? string.Empty, LdapEmailAttribute ?? string.Empty), ActorKey());
        TempData["Status"] = "LDAP settings saved.";
        return RedirectToPage(null, null, "directory");
    }

    public async Task<IActionResult> OnPostLdapSyncScheduleAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        await portalSettings.SaveLdapSyncAsync(db, new LdapSyncSettings(LdapSyncInterval ?? string.Empty), ActorKey());
        TempData["Status"] = "Directory sync schedule saved.";
        return RedirectToPage(null, null, "directory");
    }

    public async Task<IActionResult> OnPostRunDirectorySyncAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        var settings = await portalSettings.GetAllAsync(db);
        try
        {
            var result = await directoryImportService.ImportAsync(db, PortalSettingsService.ToLdap(settings), ActorKey(), DateTimeOffset.UtcNow);
            TempData["Status"] = $"Directory sync completed. Imported {result.Imported}, updated {result.Updated}, disabled {result.DisabledMissing}.";
        }
        catch (Exception ex)
        {
            TempData["Status"] = $"Directory sync failed: {ex.Message}";
        }
        return RedirectToPage(null, null, "directory");
    }

    public async Task<IActionResult> OnPostMailAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        await portalSettings.SaveMailAsync(db, new MailServerSettings(MailSmtpHost ?? string.Empty, MailSmtpPort ?? string.Empty, MailUseTls, MailFromAddress ?? string.Empty, MailFromName ?? string.Empty), ActorKey());
        TempData["Status"] = "Notification server settings saved.";
        return RedirectToPage(null, null, "notifications");
    }

    public async Task<IActionResult> OnPostNotificationTemplatesAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        await portalSettings.SaveNotificationTemplatesAsync(db, new NotificationTemplateSettings(NotificationAllUsersSubject ?? string.Empty, NotificationAllUsersBody ?? string.Empty, NotificationVaultAccessSubject ?? string.Empty, NotificationVaultAccessBody ?? string.Empty), ActorKey());
        TempData["Status"] = "Notification templates saved.";
        return RedirectToPage(null, null, "notifications");
    }

    public async Task<IActionResult> OnPostSyslogAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        await portalSettings.SaveSyslogAsync(db, new SyslogSettings(SyslogHost ?? string.Empty, SyslogPort ?? string.Empty, SyslogProtocol ?? string.Empty, SyslogEnabled), ActorKey());
        TempData["Status"] = "Syslog settings saved.";
        return RedirectToPage(null, null, "logs");
    }

    public async Task<IActionResult> OnPostSqlServerAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        if (string.IsNullOrWhiteSpace(SqlServerHost)) ModelState.AddModelError(nameof(SqlServerHost), "SQL Server host is required.");
        if (string.IsNullOrWhiteSpace(SqlServerDatabase)) ModelState.AddModelError(nameof(SqlServerDatabase), "Database name is required.");
        if (string.IsNullOrWhiteSpace(SqlServerUser)) ModelState.AddModelError(nameof(SqlServerUser), "SQL login/user is required.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        await portalSettings.SaveSqlServerAsync(db, SqlServerHost ?? string.Empty, SqlServerPort ?? string.Empty, SqlServerDatabase ?? string.Empty, SqlServerUser ?? string.Empty, SqlServerPassword, SqlServerEncrypt, SqlServerTrustServerCertificate, ActorKey());
        TempData["Status"] = "SQL Server connection settings saved. A restart or setup workflow will be required before these values become the active application database connection.";
        return RedirectToPage(null, null, "database");
    }

    public async Task<IActionResult> OnPostFederationAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        var mode = PortalSettingsService.NormalizeFederationMode(FederationMode ?? string.Empty);
        if (mode == "OIDC")
        {
            if (string.IsNullOrWhiteSpace(OidcAuthority)) ModelState.AddModelError(nameof(OidcAuthority), "OIDC authority is required when OIDC mode is selected.");
            if (string.IsNullOrWhiteSpace(OidcClientId)) ModelState.AddModelError(nameof(OidcClientId), "OIDC client ID is required when OIDC mode is selected.");
        }
        if (mode == "SAML")
        {
            if (string.IsNullOrWhiteSpace(SamlMetadataUrl)) ModelState.AddModelError(nameof(SamlMetadataUrl), "SAML metadata URL is required when SAML mode is selected.");
            if (string.IsNullOrWhiteSpace(SamlEntityId)) ModelState.AddModelError(nameof(SamlEntityId), "SAML entity ID is required when SAML mode is selected.");
        }
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        await portalSettings.SaveFederationAsync(db, mode, OidcAuthority ?? string.Empty, OidcClientId ?? string.Empty, OidcClientSecret, OidcScopes ?? string.Empty, SamlMetadataUrl ?? string.Empty, SamlEntityId ?? string.Empty, RequireExternalMfa, ActorKey());
        TempData["Status"] = "Federated identity settings saved. OIDC mode enables runtime federated sign-in while local emergency admin login remains available.";
        return RedirectToPage(null, null, "federation");
    }

    public async Task<IActionResult> OnPostSendAllUsersNotificationAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        var activeUsers = await db.Users.CountAsync(u => u.AppAccessEnabled);
        TempData["Status"] = $"Notification queued for {activeUsers} active users.";
        return RedirectToPage(null, null, "notifications");
    }

    public async Task<IActionResult> OnPostExportAllVaultsCsvAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        if (string.IsNullOrWhiteSpace(ExportAllVaultsPassphrase)) ModelState.AddModelError(nameof(ExportAllVaultsPassphrase), "Export passphrase is required.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        var export = await exportService.ExportAllVaultsCsvZipAsync(db, ActorKey(), ExportAllVaultsPassphrase ?? string.Empty, DateTimeOffset.UtcNow);
        return File(export.Content, export.ContentType, export.FileName);
    }

    public async Task<IActionResult> OnPostCreateLocalUserAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        if (string.IsNullOrWhiteSpace(CreateUsername)) ModelState.AddModelError(nameof(CreateUsername), "Username is required.");
        if (string.IsNullOrWhiteSpace(CreatePassword) || CreatePassword.Length < 12) ModelState.AddModelError(nameof(CreatePassword), "Password must be at least 12 characters long.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        await adminUserService.CreateLocalUserAsync(db, CreateUsername ?? string.Empty, CreatePassword ?? string.Empty, CreateRoles, ActorKey(), DateTimeOffset.UtcNow);
        var created = await db.Users.SingleAsync(u => u.Username == (CreateUsername ?? string.Empty).Trim());
        created.DisplayName = string.IsNullOrWhiteSpace(CreateDisplayName) ? created.Username : CreateDisplayName.Trim();
        created.Email = CreateEmail?.Trim() ?? string.Empty;
        await db.SaveChangesAsync();
        TempData["Status"] = "Local user created.";
        return RedirectToPage(null, null, "users");
    }

    public async Task<IActionResult> OnPostToggleUserAccessAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        await adminUserService.SetUserAccessAsync(db, UserKey ?? string.Empty, UserAccessEnabled, ActorKey(), DateTimeOffset.UtcNow);
        TempData["Status"] = UserAccessEnabled ? "User enabled." : "User disabled.";
        return RedirectToPage(null, null, "users");
    }

    public async Task<IActionResult> OnPostUpdateUserRolesAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        await adminUserService.UpdateUserRolesAsync(db, UserKey ?? string.Empty, UserRoles, ActorKey(), DateTimeOffset.UtcNow);
        TempData["Status"] = "User roles saved.";
        return RedirectToPage(null, null, "users");
    }

    public async Task<IActionResult> OnPostCreateGroupAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        var name = (GroupName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) ModelState.AddModelError(nameof(GroupName), "Group name is required.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        db.UserGroups.Add(new UserGroupEntity { Name = name, Description = GroupDescription?.Trim() ?? string.Empty, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        TempData["Status"] = "Group created.";
        return RedirectToPage(null, null, "users");
    }

    public async Task<IActionResult> OnPostAssignGroupUserAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        var actor = ActorKey();
        if (!await db.UserGroupMembers.AnyAsync(m => m.GroupId == GroupId && m.UserKey == GroupUserKey))
        {
            db.UserGroupMembers.Add(new UserGroupMemberEntity { GroupId = GroupId, UserKey = GroupUserKey ?? string.Empty, AddedByUserKey = actor, AddedAt = DateTimeOffset.UtcNow });
        }
        if (GroupDelegatedManager && !await db.UserGroupManagers.AnyAsync(m => m.GroupId == GroupId && m.UserKey == GroupUserKey))
        {
            db.UserGroupManagers.Add(new UserGroupManagerEntity { GroupId = GroupId, UserKey = GroupUserKey ?? string.Empty, AddedByUserKey = actor, AddedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        TempData["Status"] = GroupDelegatedManager ? "Group member and delegated manager saved." : "Group member saved.";
        return RedirectToPage(null, null, "users");
    }

    public async Task<IActionResult> OnPostDashboardWidgetAsync()
    {
        await LoadRolesAsync();
        if (!CanManageFeatureVisibility) return Forbid();
        await dashboardService.UpdateWidgetAsync(db, WidgetKey ?? string.Empty, Enabled, Title ?? string.Empty, Description ?? string.Empty, RolesCsv ?? string.Empty, SortOrder);
        TempData["Status"] = "Feature visibility saved.";
        return RedirectToPage(null, null, "visibility");
    }

    private async Task LoadAsync(string? userSearch = null)
    {
        await LoadRolesAsync();
        Widgets = await dashboardService.GetAllWidgetsAsync(db);
        var visibleWidgets = Widgets.Where(w => w.IsVisibleTo(CurrentRoles)).OrderBy(w => w.SortOrder).ToArray();
        AvailableFeatures = visibleWidgets.Select(SettingsFeatureLink.FromWidget).ToArray();
        var settings = await portalSettings.GetAllAsync(db);
        DashboardText = new DashboardTextSettings(settings[PortalSettingKeys.DashboardHeroTitle], settings[PortalSettingKeys.DashboardHeroDescription]);
        DashboardHeroTitle = DashboardText.HeroTitle;
        DashboardHeroDescription = DashboardText.HeroDescription;
        Ldap = PortalSettingsService.ToLdap(settings);
        LdapSync = PortalSettingsService.ToLdapSync(settings);
        LdapSyncInterval = LdapSync.Interval;
        Mail = PortalSettingsService.ToMail(settings);
        NotificationTemplates = PortalSettingsService.ToNotificationTemplates(settings);
        Syslog = PortalSettingsService.ToSyslog(settings);
        SqlServer = PortalSettingsService.ToSqlServer(settings);
        SqlServerHost = SqlServer.Host;
        SqlServerPort = SqlServer.Port;
        SqlServerDatabase = SqlServer.Database;
        SqlServerUser = SqlServer.User;
        SqlServerEncrypt = SqlServer.Encrypt;
        SqlServerTrustServerCertificate = SqlServer.TrustServerCertificate;
        Federation = PortalSettingsService.ToFederation(settings);
        FederationMode = Federation.Mode;
        OidcAuthority = Federation.OidcAuthority;
        OidcClientId = Federation.OidcClientId;
        OidcScopes = Federation.OidcScopes;
        SamlMetadataUrl = Federation.SamlMetadataUrl;
        SamlEntityId = Federation.SamlEntityId;
        RequireExternalMfa = Federation.RequireExternalMfa;
        ActiveDatabaseProvider = db.Database.ProviderName ?? "Unknown";
        UserSearch = userSearch?.Trim() ?? string.Empty;
        var usersQuery = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(UserSearch))
        {
            usersQuery = usersQuery.Where(u => u.Username.Contains(UserSearch) || u.DisplayName.Contains(UserSearch) || u.Email.Contains(UserSearch));
        }
        ManagedUsers = await usersQuery.OrderBy(u => u.Username).ToListAsync();
        ActiveUserCount = await db.Users.CountAsync(u => u.AppAccessEnabled);
        DirectoryUserCount = await db.Users.CountAsync(u => u.Source != UserSource.Local);
        LocalUserCount = await db.Users.CountAsync(u => u.Source == UserSource.Local);
        Groups = await db.UserGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();
        var allUsers = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.UserKey, u => u.DisplayName == string.Empty ? u.Username : u.DisplayName);
        var members = await db.UserGroupMembers.AsNoTracking().ToListAsync();
        var managers = await db.UserGroupManagers.AsNoTracking().ToListAsync();
        GroupMemberNames = members.GroupBy(m => m.GroupId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(m => allUsers.GetValueOrDefault(m.UserKey, m.UserKey)).OrderBy(n => n)));
        GroupManagerNames = managers.GroupBy(m => m.GroupId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(m => allUsers.GetValueOrDefault(m.UserKey, m.UserKey)).OrderBy(n => n)));
    }

    private Task LoadRolesAsync()
    {
        var roles = User.Claims.Where(c => c.Type == ClaimTypes.Role)
            .Select(c => Enum.TryParse<ArkanumRole>(c.Value, out var role) ? role : (ArkanumRole?)null)
            .OfType<ArkanumRole>()
            .Distinct()
            .ToArray();
        CurrentRoles = roles.Length == 0 ? [ArkanumRole.User] : roles;
        return Task.CompletedTask;
    }

    private string ActorKey() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
}

public sealed record SettingsFeatureLink(string Title, string Description, string Page, string Badge)
{
    public static SettingsFeatureLink FromWidget(DashboardWidgetEntity widget) => widget.WidgetKey switch
    {
        DashboardWidgetKeys.Vaults => new(widget.Title, widget.Description, "/Vaults/Index", "Vaults"),
        DashboardWidgetKeys.UserManagement => new(widget.Title, widget.Description, "/Users/Index", "Admin"),
        DashboardWidgetKeys.AuditLogs => new(widget.Title, widget.Description, "/Audit/Index", "Audit"),
        DashboardWidgetKeys.DirectorySync => new(widget.Title, widget.Description, "/Settings/Index", "Directory"),
        DashboardWidgetKeys.PasswordPolicy => new(widget.Title, widget.Description, "/Settings/Index", "Policy"),
        _ => new(widget.Title, widget.Description, "/Settings/Index", "Settings")
    };
}
