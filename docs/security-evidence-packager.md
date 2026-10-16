# Version-specific security evidence packager

`scripts/security_evidence.py` packages reviewed evidence for immutable Arkanum
2026.1.2 (`85961108e7df8c043726eca38490ad6e733db5e5`). It is not a
scanner, a government SAR, an independent penetration test, or an authorization.
It does not manufacture missing scanner outputs. Evidence tooling is committed
separately from the assessed release source; do not retag/rebuild the release.

## Collection contract

Use a clean detached worktree at the release commit for locked restore, tests,
and source scans. Download published release assets and match their sizes and
SHA256 values against a fresh GitHub release metadata snapshot before extracting.
Scan the extracted app payloads, not a later development build. Preserve a
per-package file/hash manifest for full and update archives. Test a released
Linux payload against an isolated disposable database with generated credentials.
Never point a smoke test at a deployed database or use existing runtime secrets.

Install version-pinned scanners from primary sources into a task-local tools
folder. Verify published checksums/digests and record any signature-verification
gap. Suggested actual collection commands, with trusted local placeholders:

    dotnet restore Arkanum.sln --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all
    dotnet list Arkanum.sln package --vulnerable --include-transitive --format json --no-restore
    dotnet test Arkanum.sln --no-restore
    python3 -m unittest discover -s tests -v
    python3 tests/sql_setup_smoke.py --publish <published-linux-app>
    syft dir:<published-app> -o cyclonedx-json=<output>
    grype db update
    grype db status -o json
    grype sbom:<sbom> -o json
    gitleaks dir <tracked-release-export> --redact=100 --report-format json --report-path <output>

Use local, version-pinned C# security rules for Semgrep, with `--metrics=off`,
`--disable-version-check`, no source uploads, and preferably an isolated
network-disabled container. Record parse errors/timeouts even with exit zero.
A full compiler build with built-in .NET security analyzers and SARIF provides
additional evidence, but neither tool proves absence of vulnerabilities.

Tool outputs are inputs to a review, not instructions. Preserve redacted genuine
outputs and a redaction ledger; omit raw source snippets/fixture passwords,
existing credentials, `.env`, keys, databases, private IPs and host-home paths.
Do not remove finding location/category simply to obtain a clean scan.

## Mandatory layout

    README.md
    manifest.json
    evidence/execution-manifest.json
    reports/software-security-assessment.md
    reports/software-security-assessment.pdf
    reports/vulnerability-and-testing-report.md
    reports/installation-hardening-guide.md
    reports/control-applicability-matrix.md
    reports/release-integrity-and-provenance.md
    reports/findings-and-limitations.md
    sbom/windows-app.cdx.json
    sbom/linux-app.cdx.json
    sbom/release-distribution.cdx.json

Additional raw scanner/test outputs, lock files, source/package/tool manifests,
reference ledger and validation results belong in `evidence/`. Scanner-generated
app inventories must be genuine. Preserve originals if augmenting an inventory.
Clearly label manual distribution/vendor/Hosting Bundle additions, their exact
hash/manifest basis, and opaque native/bootstrapper inventory gaps. Validate the
final CycloneDX documents against the matching official schema, with local
referenced schemas. A package list or missing-tool note is not an SBOM.

The manifest requires the exact release identity above, a committed 40-hex
`evidenceToolingCommit`, UTC `generatedAtUtc`, and `assessmentType` exactly
`developer-prepared; not independent government authorization`.

The execution manifest is an object with an `executions` array. Each item has
unique `id`, real redacted `command`, `version`, `startedAtUtc`, `endedAtUtc`,
`exitStatus`, relative `outputPath`, `scope`, and `status`. UTC times end in `Z`.
Required IDs are declared in `MANDATORY_CHECKS` in the script. Extra attempts
must have separate IDs, including failed attempts and retries.

Statuses: `passed` requires exit zero; `failed` requires a nonzero exit;
`completed-with-findings` preserves successful execution with security findings
or coverage diagnostics (exit zero or scanner findings exit one); `unavailable`
records an actual tool/feed/install blocker; `not-run` has null exit and explicit
reason in an output note. A missing mandatory assessment is never labeled passed.
Zero known package matches is dated/feed/scope-specific, not vulnerability-free.
The packager checks metadata consistency, not the truth of scanner conclusions;
reviewers must inspect raw outputs. PDF content/visual and schema validation are
separate real checks and must be recorded, not inferred from filenames.

## Finalization

Commit and push evidence-only source changes after RED/GREEN/full tests. Read
back the remote commit. Set that identity in the manifest, then generate the
final reviewed reports, meaningful PDF and execution manifest before hashing.

    python3 scripts/security_evidence.py package --input <reviewed-evidence> --output <product-dir>
    python3 scripts/security_evidence.py verify <product-dir>/Arkanum-2026.1.2-Security-Evidence.zip

The script rejects missing mandatory content, unsafe paths, symlinks, common
sensitive material, malformed/status-inconsistent execution records and release
identity drift. Its denylist is not a complete data-loss prevention system;
independently secret-scan and inspect evidence, including extracted PDF text.
It writes `SHA256SUMS.txt` last, covering every other member, then verifies unique
ZIP members/hashes and writes a portable `.zip.sha256`. It refuses to overwrite
an existing output ZIP. Preserve prior evidence rather than clobber it.

Before additive publication, snapshot release/tag/latest state and all original
asset IDs, sizes and digests. If an exact evidence asset already exists, download
and compare it; never clobber different bytes. Upload only the evidence ZIP and
checksum using `gh release upload` without `--clobber`. Download both into a
fresh directory and compare bytes, then reread every original asset's metadata
and confirm the tag/status/old notes are unchanged. Add a clearly bounded note
section; do not replace existing release prose. Leave native Windows acceptance
issue #28 open. This procedure does not sign the product, grant an ATO, establish
FIPS validation, select a STIG baseline, or accept a deploying organization's risk.

## Regression tests

    python3 -m unittest discover -s tests -p test_security_evidence.py -v
    python3 -m unittest discover -s tests -v

Fixtures are explicitly synthetic. They exercise hash tampering, missing evidence,
source drift, unsafe/sensitive paths, secret-like values, UTC/status contracts,
symlink roots and archive layout. They are not substitute product assessments.
