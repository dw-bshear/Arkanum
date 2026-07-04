# Arkanum Phase 2 security hardening release

Phase 2 builds on the installable feedback release by adding security controls and evidence that make Arkanum easier to review in controlled and DoD-adjacent environments. This phase still avoids unsupported certification claims; the goal is to create concrete hardening, documentation, and test evidence.

## Phase 2 tracks

### Product security controls

- Browser hardening headers on every response.
- No-store caching policy for vault, settings, user, audit, login, setup, logout, and error pages.
- Production-safe error page with a reference ID instead of developer guidance or exception details.
- Hardened authentication cookie defaults: HttpOnly, SameSite=Strict, 30-minute idle timeout, sliding expiration.
- Configurable secure-cookie policy so HTTPS deployments can force `CookieSecurePolicy.Always` while HTTP review builds can still function.

### Operational evidence

- Health check output from Phase 1 remains the installer verification primitive.
- Security header tests prove the expected hardening headers are emitted.
- Cookie-policy tests prove the session defaults are intentional.
- Error-page markup tests prove development-mode instructions are not shown to deployed users.

### Completed Phase 2 controls

- Account lockout for local accounts after repeated failed login attempts.
- Expanded audit coverage for local login failures, temporary lockout, settings updates, user access/role changes, vault membership/folder actions, and vault item create/reveal/copy.
- Syslog forwarding foundation for audit/security events over UDP with fail-open behavior if forwarding fails.

### Moved to Phase 3

- Install report generation.
- SBOM/checksum release automation.

### Remaining Phase 2/3-adjacent work

- FIPS compatibility notes and a test plan for Windows Server FIPS policy and hardened Linux hosts.
- Role separation beyond Administrator/User, such as Auditor and Vault Manager.

## Security header baseline

Arkanum emits:

```text
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
Content-Security-Policy: default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'
```

The CSP still allows inline script/style because the current Razor/static asset pipeline uses inline behaviors. A later hardening pass should remove inline script dependencies and move toward nonces or fully external scripts.

## Sensitive-page cache policy

For vault and administration pages, Arkanum emits:

```text
Cache-Control: no-store, no-cache, max-age=0
Pragma: no-cache
Expires: 0
```

This reduces the chance of sensitive vault/admin pages being preserved in browser or proxy caches.

## Error handling boundary

Production users should see only a generic error page and reference ID. Full exception information belongs in server-side logs. The error page must not show stack traces, environment-variable guidance, SQL details, file paths, connection strings, LDAP bind values, or secret material.
