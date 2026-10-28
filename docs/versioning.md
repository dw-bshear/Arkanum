# Arkanum versioning policy

## Current 1.1.1 source and the 1.0.0 transition

The current source version is **1.1.1**, using feature-based **MAJOR.MINOR.PATCH**. It is a backward-compatible password-generator security fix following the credential-history and feedback features in 1.1.0; **1.0.0** remains the start of the new version family. Publication, tagging, final-commit packaging and deployment are separate steps owned by the release operator. Source preparation is not proof that a GitHub release has been published or that native Windows acceptance has passed.

Published calendar releases, including **2026.1.2**, remain the immutable legacy family. Do not rename, delete, overwrite, or relabel their tags, releases, assets, checksums, or provenance. The Security Evidence package and evidence tooling scoped to `2026.1.2` remain scoped to that exact release: they are **not 1.0.0 security evidence**.

## Application compatibility contract

| Component | Increment when |
| --- | --- |
| **MAJOR** | A delivered change breaks the supported user/operator, configuration, integration, or upgrade contract. |
| **MINOR** | Backward-compatible features are delivered. |
| **PATCH** | Backward-compatible fixes are delivered. |

A major increment resets minor and patch to zero; a minor increment resets patch to zero. There are **no automatic annual bumps**. Optional prereleases use SemVer identifiers (`1.1.0-rc.1`); numeric identifiers sort numerically, before nonnumeric identifiers, and a final version sorts after its prereleases. Build metadata does not affect precedence. Numeric components and numeric prerelease identifiers cannot have leading zeroes.

## Actual version decision paths

- `Directory.Build.props` defines application/assembly metadata, currently `1.1.1` / `1.1.1.0`.
- Windows CLI loads `Arkanum-Version.ps1`; the Windows GUI invokes that CLI. Linux CLI invokes `arkanum_version.py`; the Linux GUI invokes that CLI. No separate GUI comparator exists.
- Both **full and update** archive installers validate target versions and existing-install transitions **before provisioning, prerequisite changes, backups, application replacement or service stops**. Read-only preflight uses the same decision. A full package cannot bypass validation by selecting install/restore over an existing instance.
- `scripts/create-release-packages.py` defaults to `1.1.1`, validates target/minimum versions and emits all four full/update manifests. `minimumInstalledVersion`, when supplied, is enforced using the same family ordering, not lexical/numeric ordering. Omission is not a claim that arbitrary legacy releases are supported.
- The existing native DEB staging builder uses the same target validator and the explicit Debian epoch described below. MSI is not a supported installer/update path.

### Supported source and action rules

| Source/target | Decision |
| --- | --- |
| Exactly legacy `2026.1.2` -> `1.0.0` (or later final/new-family version) | Upgrade, subject to independent SQL/schema/key/configuration checks. |
| Legacy `2026.1.2` -> `1.0.0` prerelease | Reject; the authorized transition starts at the final release. |
| New-family version -> higher new-family precedence | Upgrade. |
| Equal precedence, including a build-metadata-only change | Same-version reinstall via upgrade/auto, or explicit repair; preserve state and take a backup. |
| Explicit repair -> different precedence | Reject; choose upgrade for a newer version. |
| New-family -> older new-family version or legacy calendar version | Reject downgrade. |
| Missing/corrupt state, malformed version, other calendar release (`2026.1.1`, `2027.1.0`, etc.) | Fail closed before mutation. Recover/verify the actual install-state using trusted records; do not guess a source version or falsify the manifest. |

New-family major versions `1..1999` are recognized; `>=2000` is reserved to avoid interpreting unknown calendar-family numbers as new SemVer. Major zero is not a supported installed/new release family. This is an explicit Arkanum support boundary, not a change to the general SemVer standard. Supporting other historical source releases or expanding this boundary requires a separately tested compatibility decision.

### Native package constraints

The Linux DEB is a **staging vehicle**, not an unattended database/service updater. Final versions without build metadata map to Debian version `1:<product-version>` (for example `1:1.0.0`), which orders above epoch-zero `2026.1.2`; all future DEBs must retain epoch `1`. Archive names and product version remain `1.0.0`. Prereleases/build metadata are rejected by this native builder rather than silently approximated with Debian precedence; use the TAR installer for these versions.

New DEBs stage under `/usr/lib/arkanum/app`, outside the live `/opt/arkanum` destination, and the operator runs the normal CLI/GUI installer. A legacy native DEB owned `/opt/arkanum` directly: dpkg changing that layout could remove live files. Its upgrade is therefore **rejected in preinst before unpack**; native package ownership migration is not implemented. Use the full/update TAR workflow for the product transition and plan native ownership migration separately. Do not remove a legacy DEB while its owned files are still the live app.

Windows MSI remains **unsupported/staging only**, even if WiX is installed. The old template had no defined payload component group and no verified native mapping across the calendar reset. `Build-ArkanumMsi.ps1` now writes explicit staging instructions only; it must not claim a `1.0.0` MSI can replace a numerically higher legacy MSI. ZIP CLI/GUI installation is the supported Windows track.

## Preservation, preflight, and rollback

Renumbering does not create/reset a database or regenerate its Recovery Key. Upgrade/repair requires the existing key. Existing database connectivity is reused by default; explicit connection replacements are operator choices, not consequences of the version reset. Linux preserves its systemd environment and service unit (including overrides/bindings), and appsettings/web.config; Windows preserves appsettings/web.config, runtime configuration, existing IIS bindings and app-pool identity. Use the existing site's paths/identity/health URL when upgrading. Changing deployment bindings is a separate administration operation.

Both tracks retain pre-change app/config backups and reports. Linux also backs up the service unit; the key remains in its protected original file and must have its own approved recovery backup. SQL backup is an independent DBA responsibility. Preflight checks the shipped schema contract, effective runtime permissions, and the existing matching recovery sentinel using `--database-setup preflight`; it does not initialize or migrate the database. `/health` is liveness only; authenticated `/health/details` and a non-production encrypted item reveal validate readiness separately.

**Database schema version/migration state is separate from application version.** No schema version reset or migration is implied by `1.0.0`. Schema mismatch, key mismatch, connectivity, and permission failures must stop replacement independently of accepted product precedence.

Rollback is operator-controlled recovery, **not** an installer downgrade override: stop the site/service; restore app files and install-state/configuration from the matching pre-upgrade backup; preserve the matching key and bindings; restore SQL from a matching backup only if database changes require it; validate schema/sentinel, readiness and encrypted item reveal before reopening access. Keep the original immutable legacy package available. Never force a lower version over a live database merely to bypass a comparison.

## Verification and outstanding gates

`tests/test_semver_transition.py` exercises the Python/PowerShell policy with identical vectors (legacy transition, major/minor/patch, prereleases, build metadata, repair, genuine downgrades and unknown/malformed families), actual DEB build/control metadata and dpkg ordering, package manifests and installer call-site contracts. Existing Windows prerequisite tests exercise script bodies with mocked host/native boundaries; this is not native IIS installation proof. See `1.0.0-release.md` for source verification and outstanding platform evidence.

Before production deployment/publication, record actual Windows/Linux package upgrades preserving SQL contents/connectivity, encrypted item readability, key/configuration bytes and bindings, plus backup/rollback and schema/key-failure evidence on disposable targets. Native Windows acceptance **#28** and security evidence/assessment follow-up **#29** remain separate open work. Source tests, PowerShell 7 parsing, DEB staging and adopting SemVer do not close those gates. Issue **#30** tracks transition acceptance; do not mark every criterion complete based on source checks alone.
