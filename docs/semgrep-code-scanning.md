# Semgrep Community Edition in GitHub

`.github/workflows/semgrep.yml` adds a second static security scanner alongside
GitHub's existing CodeQL configuration. It requires no Semgrep account, token,
trial or subscription. The repository is public, allowing GitHub-hosted standard
Actions runners and native code-scanning results without a paid security plan.
Do not assume those terms remain the same if repository visibility changes.

## Results

- Open **Security → Code scanning** and select the **Semgrep** tool.
- Open **Actions → Semgrep CE** for scan logs and a findings/coverage summary.
- Each run retains original JSON and SARIF reports as an artifact for 30 days.
- Scans run on pushes to `main`, pull requests targeting `main`, weekly on
  Monday at 05:37 UTC, and through **Run workflow** in GitHub Actions.
- Pull requests receive a full-tree scan, not a paid diff-aware Semgrep scan.
  Standard `pull_request` permissions are used, never `pull_request_target`.
  Fork contributions can require a maintainer to approve the workflow run.

Findings are report-only: there is no `--error` option and no new required
branch-protection check. Scanner/configuration failures still fail the job.
Partial-parsing warnings do not fail the job, but appear explicitly in the
summary with affected file paths; available reports are uploaded even after
scan failure. A successful workflow means reports were generated and uploaded,
not that coverage was complete or findings were accepted.
Review the JSON `errors` array and summary before interpreting a zero count.

## Scope and reproducibility

The workflow pins Semgrep CE 1.182.0 by container digest, GitHub Actions by
commit, and the upstream `semgrep/semgrep-rules` repository by commit. It uses
these community security-rule directories:

- `csharp/dotnet/security`
- `csharp/lang/security`
- `csharp/razor/security`
- `javascript/browser/security`
- `javascript/lang/security`
- `javascript/jquery`

The upstream rules checkout retains its license. Rules are used locally without
registry requests. The scan explicitly selects the OSS engine, disables usage
metrics/version checks, mounts source read-only and runs without container
network access. Source/results are not submitted to Semgrep's hosted service;
SARIF and report artifacts are deliberately published to this public GitHub
repository. No application code or build scripts are executed by the scanner.

`.semgrepignore` excludes generated/runtime data and scan tooling, not tests or
vendored JavaScript. Semgrep also applies language support, binary detection,
Git ignore rules and its default file-size limit. Razor rule availability does
not imply complete analysis of every mixed C#/HTML template. The JSON report
records files scanned and coverage diagnostics; this is not a full application
assessment or an exact-release evidence bundle.

CE has narrower analysis than commercial engines. Findings need human triage;
zero findings are not certification, an ATO or proof of security. CodeQL stays
independent and unchanged, so differences can be reviewed without replacing it.

## Maintenance

Update scanner digest and rule commit deliberately, exercise the new combination,
and review changes in findings and coverage. Scheduled scans use these pinned
versions; they do not silently fetch a new engine or ruleset. No paid features,
autofixes or blanket vendor-rule suppressions are enabled.
