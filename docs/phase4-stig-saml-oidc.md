# Arkanum Phase 4 STIG mapping and federated identity

Phase 4 starts the compliance-evidence track. The priority is not direct smart-card integration inside Arkanum; the priority is mapping implemented controls to reviewable evidence and preparing enterprise SAML/OIDC sign-in.

## Phase 4 priorities

1. STIG/control mapping evidence.
2. OIDC runtime sign-in for modern enterprise identity providers.
3. SAML readiness for ADFS/legacy IdP environments.
4. Preserve local emergency administrator login as a break-glass path.
5. Treat smart card/PIV/CAC as an upstream IdP/MFA policy option unless a customer explicitly requires direct certificate authentication in Arkanum.

## Initial control mapping

| Mapping ID | Family | Status | Arkanum evidence |
| --- | --- | --- | --- |
| APP-AUTH-001 | Identification and Authentication | Partial | Local emergency login uses hashed passwords; account lockout is enforced; OIDC/SAML readiness settings are modeled. |
| APP-AC-001 | Access Control | Implemented | Administrator, Vault Manager, Auditor, and User role separation; auditor-only read-only enforcement; multi-role assignment. |
| APP-AU-001 | Audit and Accountability | Implemented | Login, lockout, settings changes, vault assignment, secret create/reveal/copy, and syslog forwarding foundation. |
| APP-SC-001 | System and Communications Protection | Partial | Security headers, no-store sensitive pages, SQL encryption settings, server-side AES-GCM secret encryption. |
| APP-CM-001 | Configuration Management | Partial | Install report, health checks, package inventory, SHA-256 manifest, and controlled-network release evidence. |
| APP-CP-001 | Contingency Planning | Planned | Backup/restore planning exists; tested transcript still needed. |
| APP-SI-001 | System and Information Integrity | Partial | Health endpoint, install report, syslog forwarding foundation, release evidence checksums. |

This is not a STIG certification claim. It is a working evidence map that identifies which controls are implemented, partial, inherited, or planned.

## OIDC feasibility

OIDC is the preferred first runtime federation path because ASP.NET Core has maintained first-party OpenID Connect middleware. Suitable IdPs include Entra ID, ADFS 2019+, Keycloak, Okta, Ping, and other OIDC-compatible providers.

Built OIDC runtime implementation items:

- Added OpenID Connect authentication middleware.
- Added `/Login` OIDC challenge path when OIDC mode is configured.
- Maps IdP subject/username/email claims to existing active non-local Arkanum users.
- Blocks federated assertions from impersonating local emergency administrators.
- Keeps Arkanum roles administrator-controlled by default; IdP group/role claims are not automatically promoted.
- Preserves `/Login` local emergency admin path.

Runtime implementation items still to build:

- Validate real IdP callback/logout behavior against Entra ID, ADFS, Keycloak, or the selected customer IdP.
- Add deployment-specific redirect URI guidance and certificate/trust requirements.

## SAML feasibility

SAML is feasible but likely requires either a supported third-party ASP.NET Core SAML package or a reverse-proxy/IdP integration pattern. It remains important for ADFS and older enterprise identity programs.

Required runtime implementation items still to build:

- Select SAML middleware/package or reverse-proxy integration approach.
- Store metadata URL/entity ID/certificate validation settings.
- Map NameID/UPN/email claims to stable Arkanum users.
- Preserve local emergency admin login.
- Validate logout/session behavior in the target IdP environment.

## Smart card/PIV/CAC stance

For DoD-adjacent networks, smart card sign-in can often be satisfied by the enterprise IdP. Arkanum should accept the resulting SAML/OIDC assertion rather than directly validating smart-card certificates inside the app. Direct certificate authentication can remain an optional future path if a customer requires it.

## Implemented in this slice

- Added machine-readable compliance/control mapping catalog in code.
- Added compliance mappings and federation readiness to `/install-report`.
- Added SAML/OIDC settings in Settings.
- Added first-pass OIDC runtime sign-in with existing-user gating.
- Added federation setting persistence without logging secrets.
- Added tests for control catalog coverage, install-report exposure, OIDC/SAML setting redaction, OIDC claim mapping, local-admin impersonation blocking, and login markup.
