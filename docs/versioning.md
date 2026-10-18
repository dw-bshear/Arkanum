# Arkanum versioning policy

## Status and transition

Arkanum is adopting feature-based **MAJOR.MINOR.PATCH** application versioning. The current published release and UI-review baseline remains **2026.1.2**, part of the historical calendar-prefixed version family. The next planned release is **1.0.0**, after the design review and resulting design changes. **1.0.0 is not released.**

This document records the agreed policy and future migration requirements only. It does not change application behavior, version defaults, installers, packages, or deployment state. Existing release notes remain historical records. Implementation and required verification are tracked in [issue #30](https://github.com/dw-bshear/Arkanum/issues/30); publication requires separate authorization.

## Application compatibility contract

For the new version family, version numbers describe delivered changes to the supported application contract: user/operator workflows, configuration and integration interfaces, and supported installation/upgrade behavior.

| Component | Increment when |
| --- | --- |
| **MAJOR** | A change breaks the supported application contract or requires an incompatible operator/integration change. |
| **MINOR** | Backward-compatible features are added. |
| **PATCH** | Backward-compatible fixes are delivered. |

A major increment resets minor and patch to zero; a minor increment resets patch to zero. Optional prerelease identifiers may qualify a planned version, for example `1.0.0-rc.1`; a prerelease is not the final release and sorts before that version's final release within the new family.

There are **no future automatic annual bumps**. Neither a new calendar year nor elapsed time alone changes a version. Version `1.0.0` starts the new family; it does not rename or replace `2026.1.2`, and does not by itself certify security, native installer acceptance, or production readiness.

## Legacy-to-SemVer migration gate

Legacy calendar-prefixed numbers must be treated as a distinct historical family even though their shape can be parsed as three numeric components. A raw numeric or lexical comparison would incorrectly treat `2026.1.2` -> `1.0.0` as a downgrade. Before shipping the new family, implement and test explicit family-aware transition logic across all version comparison/update decision paths.

The authorized transition from legacy `2026.1.2` to the planned `1.0.0` must be recognized as an upgrade. Do not accomplish this by disabling genuine downgrade protection. Unknown or ambiguous versions must be handled safely; define supported source versions, same-version repair/reinstall, and rollback behavior explicitly. Windows/Linux CLI and GUI installers, package manifests, release tooling, and supported native package metadata must agree on the transition. Native package-manager ordering constraints require their own verified handling.

No compatibility implementation is claimed by this document. Do not attempt a legacy-to-1.0.0 deployment based solely on the new numbering policy.

## Preservation and database compatibility

- Preserve all older tags, releases, release assets, checksums, and provenance unchanged. Do not relabel, delete, or overwrite them to make numbering appear sequential.
- Preserve existing configuration, recovery/master keys, encrypted data, database contents and connectivity settings, and site/service bindings, including IIS/systemd settings where applicable. Renumbering must not regenerate keys, reinitialize databases, or reset a site.
- Retain upgrade preflight, pre-upgrade backups, health and recovery-key validation, and documented rollback safeguards.
- Keep **database schema version and migration state separate from application version**. An application version is not a schema version. Renumbering alone does not imply a schema migration or prove schema compatibility; check and test schema compatibility independently.

## Required verification before release

Issue #30 must record passing focused regression and integration evidence before the new family is published:

- Family-aware legacy `2026.1.2` -> `1.0.0` upgrade recognition, including no false numeric-downgrade detection.
- New-family major/minor/patch ordering, optional prerelease ordering, same-version repair, true downgrade rejection, and safe rejection/handling of malformed or unknown versions.
- Coverage of every version comparison/update decision path, including supported native package constraints.
- Windows/Linux package and installer upgrades preserving existing keys, database contents/connectivity, configuration, and bindings; backup/rollback validation and separate schema compatibility tests.
- Native platform acceptance evidence where required, not merely cross-platform source or package checks.

Native Windows acceptance [#28](https://github.com/dw-bshear/Arkanum/issues/28) and security evidence/assessment follow-up [#29](https://github.com/dw-bshear/Arkanum/issues/29) remain separate open work; adopting this policy does not close them.
