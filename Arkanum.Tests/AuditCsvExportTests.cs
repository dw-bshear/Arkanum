using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Pages.Audit;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace Arkanum.Tests;

public class AuditCsvExportTests
{
    [Fact]
    public void AuditPageOffersDiscoverableCsvExportWithoutPdfOrZip()
    {
        var root = ProjectRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Audit", "Index.cshtml"));

        Assert.Contains("asp-page-handler=\"ExportCsv\"", markup);
        Assert.Contains("Download audit CSV", markup);
        Assert.Contains("text/csv", markup);
        Assert.DoesNotContain("PDF", markup);
        Assert.DoesNotContain("ZIP", markup);
    }

    [Fact]
    public async Task AuditCsvExportIncludesStableHeadersAndEscapedRows()
    {
        await using var db = CreateDb(nameof(AuditCsvExportIncludesStableHeadersAndEscapedRows));
        var older = DateTimeOffset.Parse("2026-07-29T10:15:00Z");
        var newer = DateTimeOffset.Parse("2026-07-30T11:30:00Z");
        db.AuditEvents.AddRange(
            new AuditEventEntity { Id = "old", Type = AuditEventType.LoginSucceeded, ActorUserKey = "admin", Target = "Portal", OccurredAt = older, Message = "Signed in." },
            new AuditEventEntity { Id = "new", Type = AuditEventType.SettingsChanged, ActorUserKey = "auditor", Target = "Target \"quoted\"", OccurredAt = newer, Message = "Changed line one\nline two." });
        await db.SaveChangesAsync();
        var service = new AuditReportExportService();

        var export = await service.ExportAuditCsvAsync(db, "auditor", DateTimeOffset.Parse("2026-07-30T12:00:00Z"));
        var csv = Encoding.UTF8.GetString(export.Content);

        Assert.Equal("Arkanum-AuditReport-20260730120000.csv", export.FileName);
        Assert.Equal("text/csv", export.ContentType);
        Assert.StartsWith("OccurredAtUtc,Type,ActorUserKey,Target,Message", csv);
        Assert.Contains("\"2026-07-30T11:30:00.0000000+00:00\",\"SettingsChanged\",\"auditor\",\"Target \"\"quoted\"\"\",\"Changed line one line two.\"", csv);
        Assert.Contains("\"2026-07-29T10:15:00.0000000+00:00\",\"LoginSucceeded\",\"admin\",\"Portal\",\"Signed in.\"", csv);
        Assert.True(csv.IndexOf("SettingsChanged", StringComparison.Ordinal) < csv.IndexOf("LoginSucceeded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuditCsvExportOrdersRowsWhenBackedBySqlite()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"arkanum-audit-csv-{Guid.NewGuid():n}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ArkanumDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            await using var db = new ArkanumDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.AuditEvents.AddRange(
                new AuditEventEntity { Id = "old", Type = AuditEventType.LoginSucceeded, ActorUserKey = "admin", Target = "Portal", OccurredAt = DateTimeOffset.Parse("2026-07-29T10:15:00Z"), Message = "Signed in." },
                new AuditEventEntity { Id = "new", Type = AuditEventType.SettingsChanged, ActorUserKey = "auditor", Target = "Audit", OccurredAt = DateTimeOffset.Parse("2026-07-30T11:30:00Z"), Message = "Exported." });
            await db.SaveChangesAsync();
            var service = new AuditReportExportService();

            var export = await service.ExportAuditCsvAsync(db, "auditor", DateTimeOffset.Parse("2026-07-30T12:00:00Z"));
            var csv = Encoding.UTF8.GetString(export.Content);

            Assert.True(csv.IndexOf("SettingsChanged", StringComparison.Ordinal) < csv.IndexOf("LoginSucceeded", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task AuditCsvPageHandlerRequiresAuditExportPermission()
    {
        await using var db = CreateDb(nameof(AuditCsvPageHandlerRequiresAuditExportPermission));
        var service = new AuditReportExportService();
        var standardUserModel = new IndexModel(db)
        {
            PageContext = PageContextFor("user-key", ArkanumRole.User)
        };

        var forbidden = await standardUserModel.OnPostExportCsvAsync();

        Assert.IsType<ForbidResult>(forbidden);

        var auditorModel = new IndexModel(db)
        {
            PageContext = PageContextFor("auditor-key", ArkanumRole.User, ArkanumRole.Auditor)
        };

        var result = await auditorModel.OnPostExportCsvAsync();
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.EndsWith(".csv", file.FileDownloadName);
    }

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ArkanumDbContext(options);
    }

    private static PageContext PageContextFor(string userKey, params ArkanumRole[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userKey) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role.ToString())));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        return new PageContext { HttpContext = http };
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arkanum.Web", "Arkanum.Web.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate project root.");
    }
}
