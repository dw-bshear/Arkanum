# Sprint 1 review and execution status

## Scope distinction

The parent issue and Sprint 1 remain open. A passing policy/cryptographic review is not native Windows acceptance, an end-user application, a beta release, or production readiness.

The approved non-production Windows lab is now domain joined. Directory provenance was independently collected through authenticated, CA-and-hostname-verified LDAPS. Operational identities, certificates, endpoints and raw transcripts remain in ignored/private artifacts; they are not public source evidence.

## Verified execution

| Layer | Latest verified result | Meaning |
| --- | --- | --- |
| Cross-boot core | 131 passed, zero failed/skipped | Policy/crypto decisions including ordinary boot transitions, preserved expiration, rollback latch and controlled expiry cases |
| Synthetic HTTPS proof server | 51 passed, zero failed/skipped | Real HTTPS/mTLS, signatures, actual 90-second expiry, refresh renewal, membership/device separation, sticky deny, independent enrollment/state loss handling and unchanged portal isolation |
| Existing web regression | 266 passed, zero failed/skipped | Existing web behavior remained passing; no production instance was changed |
| Native primitive probe under a standard domain token | 11 checks passed, actual elevation false and administrator role disabled | Primitive Windows identity/crypto/self-spawn pipe checks only; not the installed broker/shared-cache proof |

An independent reviewer verified the frozen core/protocol/server source hashes, reran the 131 core and 51 server tests, and ran six additional scratch-only edge-case probes. That review found no blocking issue **within the core/protocol/synthetic proof-server scope**. It does not approve the whole Sprint 1 architecture or Windows integration.

## Native blocker and correction

The first installed broker failed before generating enrollment artifacts. Protected diagnostics identified machine CNG key creation under the virtual service identity returning `NTE_PERM`. A separate administrator-context diagnostic reproduced the same key-creation parameters successfully without exporting private keys or weakening ACLs.

The correction moves one-time machine-key provisioning to the elevated installer. The running broker remains a virtual-account service; no SYSTEM runtime substitution, administrator membership, broad keystore ACL, reusable domain password, user cookie, or server recovery-key distribution is permitted. Native proof of this revised initialization and its subsequent service/reader lifecycle is still required.

## Remaining acceptance

- Install/start under the actual virtual service account; validate key properties and effective file/key/token ACLs under independent standard users.
- Real device-authenticated refresh with zero interactive users signed in, followed by first Go access by the authorized domain user.
- Unassigned domain, auditor-only, same-name local, forged IPC identity, remote pipe and wrong-broker denial.
- Failed-refresh nonrenewal, signed-deny persistence and rejection of stale/invalid updates.
- Actual disconnected restart/repeated restart with unchanged signed expiry, and shutdown/startup across expiry with denial.
- Session lock/logoff/disconnect handling and relevant native sleep/time behavior.
- Final independent Windows-source review, sanitized acceptance evidence and source publication.

## Explicit limits

Normal powered-off time relies on RTC progress. Standard-user isolation does not defeat administrator/SYSTEM/service compromise, a coordinated trusted-state snapshot restore, or an administrator freezing/rewinding RTC and state together. Server file contents are flushed and replacement is atomic at the process level, but directory metadata/power-loss durability has not been qualified. Neither process-interruption tests nor software-only state protection imply a hardware anti-rollback counter or power-loss-safe storage.
