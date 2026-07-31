namespace Arkanum.Tests;

public sealed class Issue018OperationalEndpointAuthorizationTests
{
    [Fact]
    public void ProgramProtectsDetailedOperationalEndpointsWithAdminOrAuditorPolicy()
    {
        var program = ReadProgram();

        Assert.Contains("AddPolicy(\"OperationalReadiness\"", program);
        Assert.Contains("RequireAuthenticatedUser()", program);
        Assert.Contains("RequireRole(nameof(ArkanumRole.Administrator), nameof(ArkanumRole.Auditor))", program);
        Assert.Contains("app.MapGet(\"/install-report\"", program);
        Assert.Contains(".RequireAuthorization(\"OperationalReadiness\")", program);
        Assert.Contains("app.MapGet(\"/health/details\"", program);
        Assert.Matches("/install-report[\\s\\S]*RequireAuthorization\\(\"OperationalReadiness\"\\)", program);
        Assert.Matches("/health/details[\\s\\S]*RequireAuthorization\\(\"OperationalReadiness\"\\)", program);
        Assert.DoesNotContain("app.MapGet(\"/install-report\", async (ArkanumDbContext db, InstallReportService reports, CancellationToken cancellationToken) =>\r\n{\r\n    var report = await reports.BuildAsync(db, cancellationToken);\r\n    return Results.Ok(report);\r\n});", program);
    }

    [Fact]
    public void AnonymousHealthEndpointReturnsOnlyMinimalLivenessWithoutOperationalDetails()
    {
        var program = ReadProgram();
        var healthRoute = ExtractMapGetBlock(program, "/health");

        Assert.Contains("ArkanumLivenessResponse", program);
        Assert.Contains("Results.Ok(new ArkanumLivenessResponse", healthRoute);
        Assert.DoesNotContain("ArkanumDbContext", healthRoute);
        Assert.DoesNotContain("ArkanumHealthCheckService", healthRoute);
        Assert.DoesNotContain("CheckAsync", healthRoute);
        Assert.DoesNotContain("Checks", healthRoute);
        Assert.DoesNotContain("ProviderName", healthRoute);
        Assert.DoesNotContain("InstallReport", healthRoute);
    }

    [Fact]
    public void DetailedEndpointNamesAvoidAnonymousOperationalReconnaissance()
    {
        var program = ReadProgram();

        Assert.Contains("/health/details", program);
        Assert.DoesNotContain("return response.IsHealthy ? Results.Ok(response) : Results.Json(response", ExtractMapGetBlock(program, "/health"));
        Assert.Contains("return response.IsHealthy ? Results.Ok(response) : Results.Json(response", ExtractMapGetBlock(program, "/health/details"));
    }

    private static string ExtractMapGetBlock(string program, string route)
    {
        var start = program.IndexOf($"app.MapGet(\"{route}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Route {route} was not found.");
        var next = program.IndexOf("app.MapGet(\"", start + 1, StringComparison.Ordinal);
        var staticAssets = program.IndexOf("app.MapStaticAssets", start, StringComparison.Ordinal);
        var end = next >= 0 ? Math.Min(next, staticAssets >= 0 ? staticAssets : next) : staticAssets;
        Assert.True(end > start, $"Could not find end of route {route}.");
        return program[start..end];
    }

    private static string ReadProgram() => File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Program.cs"));

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arkanum.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate Arkanum.sln.");
    }
}
