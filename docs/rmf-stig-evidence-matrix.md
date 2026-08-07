# Arkanum RMF/STIG evidence matrix

This matrix is a review-ready evidence map for a customer RMF/ATO package. It does not assert that Arkanum by itself has an approved baseline, a DISA approval, or a formal accreditation outcome. The product boundary is the Arkanum ASP.NET Core application, its installer scripts, release artifacts, and operating procedures. Platform, identity, database, logging, vulnerability-management, and network controls are documented as inherited when they belong to the deploying organization.

## Deployment boundary and inherited baselines

Arkanum should be evaluated as an application layered on inherited controls from:

- Windows Server 2022 STIG for the host operating system, local security policy, file-system ACLs, certificate store, and eventing baseline.
- IIS/Web Server STIG for the Windows hosting path, TLS bindings, app-pool identity isolation, request filtering, and server logging.
- SQL Server STIG for database engine configuration, encryption in transit, backup handling, account provisioning, table permissions, and audit-row immutability controls.
- Enterprise IdP/PIV/MFA controls for CAC/PIV enforcement, MFA policy, credential lifecycle, account disablement, and assertion trust.
- SIEM/central logging controls for log transport, retention, correlation, alerting, and review cadence.
- NIST SP 800-53 / RMF control families used by the authorizing organization.
- DISA Application Security and Development STIG concepts where application-development evidence is applicable.
- Web Application SRG concepts where web app session, transport, authentication, audit, and error-handling evidence is applicable.

## Status definitions

| Status | Meaning | Evidence expectation |
| --- | --- | --- |
| Implemented | Arkanum contains the application behavior, tests, docs, or scripts needed for this control objective inside the product boundary. | Link to code, tests, installer/script behavior, generated reports, or procedure. |
| Partial | Arkanum implements part of the objective, but the deployment must add configuration, validation, or organizational evidence. | Product evidence plus site-specific closure notes. |
| Inherited | The objective is primarily satisfied by the host, database, IdP, SIEM, network, or organizational process. | Reference the inherited control owner and capture deployment evidence. |
| Planned | The objective is known and tracked but not yet complete in the product boundary. | Link to roadmap, issue, or release gate. |
| Not Applicable | The objective does not apply to the Arkanum product boundary or is intentionally excluded. | Document the reason and the boundary owner if applicable. |

## Control evidence matrix

| Mapping ID | Control family / baseline | Status | Boundary | Arkanum evidence links | Deployment evidence to attach |
| --- | --- | --- | --- | --- | --- |
| AC-APP-001 | NIST SP 800-53 AC; Web Application SRG access enforcement | Implemented | Arkanum application | Role-aware dashboard and access boundaries in `README.md`; object-boundary tests in `Arkanum.Tests/Issue017ObjectAuthorizationTests.cs`; role fallback hardening in `Arkanum.Tests/Issue016RoleFallbackTests.cs`. | User/role approval process, production role assignment export, privileged-user review cadence. |
| AC-APP-002 | Least privilege and separation of duties | Implemented | Arkanum application + SQL provisioning scripts | Administrator/Vault Manager/Auditor/User roles in `README.md`; least-privilege SQL grants in `installers/sql/provision-arkanum-sql.sql`; installer runtime tests in `Arkanum.Tests/Issue024InstallerRuntimeHardeningTests.cs`. | Evidence that the runtime SQL login is not a schema owner and that site administrators are reviewed. |
| IA-APP-001 | Identification and authentication; Enterprise IdP/PIV/MFA controls | Partial | Arkanum application plus inherited enterprise IdP | Local emergency administrator login, OIDC readiness, and local-admin impersonation blocking in `docs/phase4-stig-saml-oidc.md` and related tests. | IdP metadata, PIV/MFA policy proof, redirect URI approval, disabled-account synchronization evidence. |
| IA-APP-002 | Local emergency account controls | Implemented | Arkanum application | Local password hashing, lockout behavior, separate manual login path, and login separation tests. | Emergency-account owner approval, break-glass storage procedure, periodic login test record. |
| AU-APP-001 | Audit and accountability | Implemented | Arkanum application | Append-only audit rows, sequence/hash-chain integrity metadata, audit-forwarding paths, and tests in `Arkanum.Tests/Issue022AuditComplianceTests.cs`; evidence guide in `docs/audit-compliance-evidence.md`. | SIEM destination, retention approval, exported hash-chain verification, reviewer sign-off. |
| AU-INH-001 | SIEM/central logging controls | Inherited | Enterprise logging platform | Arkanum supports TLS syslog, Windows Event Log, or an approved SIEM forwarder path; `/health` and `/install-report` avoid secret disclosure. | Collector configuration, TLS certificate trust, alert rules, retention, monitoring owner. |
| CM-APP-001 | Configuration management and secure baseline | Partial | Arkanum installers, app config, inherited host baseline | Installer/runtime hardening in `docs/installer-runtime-hardening.md`; SQL setup scripts; protected `_FILE` configuration paths; release evidence script output. | Windows Server 2022 STIG checklist, IIS/Web Server STIG checklist, SQL Server STIG checklist, site-specific configuration baseline. |
| CM-REL-001 | Release integrity and change evidence | Implemented | Arkanum release artifacts | `scripts/generate-phase3-release-evidence.sh`, `scripts/generate-rmf-evidence-bundle.sh`, package locks, SBOM/vulnerability/checksum evidence. | Change ticket, release approval, media transfer record, post-transfer checksum verification. |
| CP-APP-001 | Contingency planning and recovery | Partial | Arkanum procedures plus SQL/database operations | Site-wide recovery model in `README.md`; `docs/backup-and-restore.md`; installer restore mode. | Backup transcript, restore transcript, RPO/RTO approval, protected recovery-key handling proof. |
| SC-APP-001 | System and communications protection | Partial | Arkanum application and hosting stack | HTTPS posture, secure cookie settings, no-store sensitive pages, server-side AES-GCM secret encryption, authenticated vault export encryption in `docs/vault-export-validation.md`. | TLS certificate chain, IIS binding export, SQL encryption settings, FIPS-mode decision if required by site policy. |
| SI-APP-001 | System and information integrity | Implemented | Arkanum application and release process | `/health`, `/install-report`, vulnerability report generation, lock-file restore, checksums, SBOM path. | Vulnerability scan review, exception approvals, patch cadence, dependency feed approval. |
| SA-APP-001 | DISA Application Security and Development STIG / secure development evidence | Partial | Arkanum source and release process | Issue-driven security tests, package locks, code review notes, release evidence artifacts, README language guardrails. | Organization secure-development process mapping, static-analysis results, reviewer approval records. |
| RA-INH-001 | Risk assessment / RMF authorization | Inherited | Customer RMF/ATO process | This matrix and the offline release evidence bundle are designed to feed the customer package. | Control assessor notes, POA&M, risk acceptance, authority-to-operate decision. |
| IR-INH-001 | Incident response | Inherited | Customer SOC/IR process | Arkanum emits audit and health evidence without secrets and supports centralized forwarding. | IR runbook, SIEM correlation rule, escalation matrix, tabletop evidence. |
| MA-INH-001 | Maintenance | Inherited | Customer maintenance process | Air-gapped update process and release integrity evidence in `docs/offline-release-evidence-bundle.md`. | Maintenance window approval, offline media handling, rollback evidence. |
| PE-INH-001 | Physical and environmental protection | Not Applicable | Outside Arkanum product boundary | No product behavior implements facility controls. | Datacenter/facility authorization package if required. |

## Evidence-link expectations

Each release evidence package should include links or copies for:

1. Tests proving application behavior (`Arkanum.Tests/*`).
2. Code implementing access, audit, crypto, health, install report, and settings behavior (`Arkanum.Web/*`).
3. Installer scripts and SQL setup scripts (`installers/*`).
4. Health and install report output captured from the deployed instance.
5. Release artifacts, manifests, checksums, package inventory, SBOM, vulnerability report, and lock files.
6. Operational procedures for install, hardening, audit forwarding, backup/restore, air-gapped update, offline package cache, and vulnerability exceptions.

## Language guidance

Use wording such as `review-ready evidence for the customer's RMF/ATO package`, `control evidence map`, or `deployment boundary mapping`. Do not describe Arkanum as formally approved by a government authority unless the deploying organization supplies that separate authorization decision.
