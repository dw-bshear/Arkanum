# Audit compliance evidence

Arkanum audit records are application append-only: the web application blocks UPDATE and DELETE attempts against `AuditEvents`, stamps each row with a sequence number, and links rows with a SHA-256 hash chain (`PreviousIntegrityHash` -> `IntegrityHash`). The hash chain is evidence that exported rows have not been reordered or modified after insertion.

## Deployment retention

Set retention per deployment or authorizing-official requirement instead of treating the application default as policy:

- `Arkanum:AuditRetentionDays` in configuration.
- `ARKANUM_AUDIT_RETENTION_DAYS` when using environment-variable configuration.

The default remains 90 days for development/review instances. Production sites should document the approved retention period, archive cadence, and who can approve purge/destruction.

## Database protection expectation

Application controls are not a replacement for database policy. Production SQL Server deployments should grant the Arkanum runtime account INSERT and SELECT for `AuditEvents` but deny UPDATE and DELETE. Document the exact GRANT/DENY statements or an inherited database immutability control in the deployment evidence package.

## Forwarding evidence

Use TLS syslog where the collector supports it. If the environment standardizes on Windows Event Log forwarding or an approved SIEM forwarder agent, document that inherited control, agent configuration, destination, and review cadence. Health checks report the last audit-forwarding failure without including audit event identifiers, message contents, or secret material.

## Archive/export/restore evidence

For each accreditation or operational evidence package, include:

1. The configured retention value and archive schedule.
2. A CSV or database export that preserves `Sequence`, `PreviousIntegrityHash`, `IntegrityHash`, and `IntegrityAlgorithm`.
3. A verification note for the exported hash chain.
4. The archive/export/restore procedure used to prove audit rows can be restored and re-verified.
5. The database deny UPDATE and DELETE control or the inherited immutable logging control.
