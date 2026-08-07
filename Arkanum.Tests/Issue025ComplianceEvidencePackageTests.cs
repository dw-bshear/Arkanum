namespace Arkanum.Tests;

public sealed class Issue025ComplianceEvidencePackageTests
{
    [Fact]
    public void ControlEvidenceMatrixUsesReviewReadyStatusesAndNoUnsupportedCertificationClaims()
    {
        var root = ProjectRoot();
        var matrixPath = Path.Combine(root, "docs", "rmf-stig-evidence-matrix.md");

        Assert.True(File.Exists(matrixPath), "Issue #25 requires a DoD/RMF/STIG control evidence matrix.");
        var matrix = File.ReadAllText(matrixPath);

        foreach (var status in new[] { "Implemented", "Partial", "Inherited", "Planned", "Not Applicable" })
        {
            Assert.Contains($"| {status} |", matrix);
        }

        foreach (var inheritedBoundary in new[]
        {
            "Windows Server 2022 STIG",
            "IIS/Web Server STIG",
            "SQL Server STIG",
            "Enterprise IdP/PIV/MFA",
            "SIEM/central logging",
            "NIST SP 800-53",
            "DISA Application Security and Development STIG",
            "Web Application SRG"
        })
        {
            Assert.Contains(inheritedBoundary, matrix);
        }

        Assert.Contains("RMF/ATO package", matrix);
        Assert.Contains("review-ready evidence", matrix);
        Assert.DoesNotContain("DoD certified", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("STIG compliant", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certified baseline", matrix, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseEvidenceBundleDocumentsRequiredArtifactsAndOfflineProcesses()
    {
        var root = ProjectRoot();
        var bundlePath = Path.Combine(root, "docs", "offline-release-evidence-bundle.md");

        Assert.True(File.Exists(bundlePath), "Issue #25 requires an offline release evidence bundle guide.");
        var bundle = File.ReadAllText(bundlePath);

        foreach (var required in new[]
        {
            "signed artifacts or signing plan",
            "SBOM",
            "checksums",
            "vulnerability report",
            "install transcript",
            "backup/restore transcript",
            "hardening guide",
            "air-gapped update/patch process",
            "offline NuGet/cache process",
            "vulnerability exception process"
        })
        {
            Assert.Contains(required, bundle, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("scripts/generate-rmf-evidence-bundle.sh", bundle);
        Assert.Contains("docs/rmf-stig-evidence-matrix.md", bundle);
        Assert.Contains("docs/installer-runtime-hardening.md", bundle);
        Assert.Contains("docs/audit-compliance-evidence.md", bundle);
    }

    [Fact]
    public void EvidenceBundleScriptCollectsMatrixReleaseEvidenceTranscriptsAndHardeningDocs()
    {
        var root = ProjectRoot();
        var scriptPath = Path.Combine(root, "scripts", "generate-rmf-evidence-bundle.sh");

        Assert.True(File.Exists(scriptPath), "Issue #25 requires a repeatable evidence-bundle collection script.");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("rmf-stig-evidence-matrix.md", script);
        Assert.Contains("offline-release-evidence-bundle.md", script);
        Assert.Contains("generate-phase3-release-evidence.sh", script);
        Assert.Contains("INSTALL_TRANSCRIPT", script);
        Assert.Contains("BACKUP_RESTORE_TRANSCRIPT", script);
        Assert.Contains("SIGNING_EVIDENCE", script);
        Assert.Contains("SHA256SUMS.txt", script);
        Assert.Contains("docs/installer-runtime-hardening.md", script);
        Assert.Contains("docs/audit-compliance-evidence.md", script);
        Assert.DoesNotContain("/home/mannie", script);
    }

    [Fact]
    public void ReadmeLinksIssue25EvidencePackageWithoutCertificationLanguage()
    {
        var readme = File.ReadAllText(Path.Combine(ProjectRoot(), "README.md"));

        Assert.Contains("rmf-stig-evidence-matrix.md", readme);
        Assert.Contains("offline-release-evidence-bundle.md", readme);
        Assert.Contains("review-ready", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DoD certified", readme, StringComparison.OrdinalIgnoreCase);
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
