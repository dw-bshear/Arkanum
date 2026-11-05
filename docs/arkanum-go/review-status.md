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
| Installed Windows broker after response-acknowledgement correction | 30 reads + 30 status checks passed under the actual unlocked non-elevated domain token | Same reused local service pipe; no relaxed identity/session/lease checks |
| Native session boundaries | Locked and disconnected reads denied; unlock/read allowed | CLI/native broker boundary, not WPF display/clipboard behavior |
| Native effective permissions | Standard authorized user could query the exact service identity, but could not assign/duplicate/impersonate its token, terminate/read memory/duplicate process handles, open either machine key, read cache or run provisioning successfully | Actual standard token; administrators/SYSTEM remain trusted |
| Windows PowerShell 5.1 native-command wrapper | Stderr with zero exit accepted; nonzero and missing executable rejected; error preference restored | Only extracted wrapper function executed; no installer/provisioning side effects |

An independent reviewer verified the frozen core/protocol/server source hashes, reran the 131 core and 51 server tests, and ran six additional scratch-only edge-case probes. That review found no blocking issue **within the core/protocol/synthetic proof-server scope**. It does not approve the whole Sprint 1 architecture or Windows integration.

## Native blockers corrected

The first installed broker failed before generating enrollment artifacts. Protected diagnostics identified machine CNG key creation under the virtual service identity returning `NTE_PERM`. A separate administrator-context diagnostic reproduced the same key-creation parameters successfully without exporting private keys or weakening ACLs.

One-time machine-key provisioning now runs in the elevated installer, with a protected one-use public receipt and service-only existing-key opening. Actual initialization, virtual-account runtime, distinct protected nonexportable machine keys and zero-interactive-user mTLS refresh executed successfully. No SYSTEM runtime substitution, administrator membership, broad keystore ACL, reusable domain-password sync, user cookie or server recovery-key distribution was used.

A later authorized native reader failure was reproduced despite a valid matching decryptable package and unlocked session. Immediate pipe disconnect could discard unread queued bytes or race the reader's final server validation. A bounded post-validation acknowledgement now keeps the same pipe instance connected until consumption. The native reader changed from exit 10 to exit 0. Temporary identity-hash/package-count diagnostic writes were removed.

The final hardened broker also bounds busy-only connection retries, recovers recognized peer-close states on the same pipe instance, and distinguishes empty from truncated frames. Actual Windows contracts passed **36/36**, with zero failures/skips. The installed final build passed **60/60** alternating read/status calls, four abrupt/partial/overlapping-client recovery cases, fresh unassigned-domain and same-name-local boundary batches, zero-interactive-user refresh, session lock/disconnect denial and unlock/reconnect read, and offline service restart without sequence/expiry renewal. Managed broker SHA-256: `c9dc2f7ff40e9b04e49f4bd02432cd2397d45c9482bfe457577d923ecdc315f9`, independently matched between the tested contract package and installed service. Key/root ACL readback retained the same distinct nonexportable keys; temporary acceptance tasks, network isolation rules and recovery tasks were read back as absent.

The final independent Windows-source review verified all 28 frozen source hashes before/after review, built with zero warnings/errors, reran 35 portable contract checks (one native-only check skipped on Linux) and 22 self-checks, and found no blocker in the proof scope. A separate six-file primitive/support-test review also passed. Parent readback confirmed both reviewed manifests still match the working source. These are scoped source approvals, not full Sprint 1 acceptance.

## Remaining acceptance


- Native auditor-only case, signed-deny persistence and remaining stale/invalid update, state corruption/replay and key lifecycle cases; retain the executed unassigned-domain/local-user/forged-SID/remote/impostor results with their actual build scope.
- Offline cached Windows domain sign-in after zero-user refresh: an authorized read in an existing domain session with blocked paths passed, but that is not cached sign-in after reboot or independent first-launch provenance.
- Actual disconnected restart/repeated restart with unchanged signed expiry, and shutdown/startup across expiry with denial.
- Relevant native sleep/time behavior and session changes during pending responses beyond the executed lock/disconnect cases.
- Acceptance review of remaining native evidence. Reviewed source publication does not close these gates.

## Explicit limits

Normal powered-off time relies on RTC progress. Standard-user isolation does not defeat administrator/SYSTEM/service compromise, a coordinated trusted-state snapshot restore, or an administrator freezing/rewinding RTC and state together. Server file contents are flushed and replacement is atomic at the process level, but directory metadata/power-loss durability has not been qualified. Neither process-interruption tests nor software-only state protection imply a hardware anti-rollback counter or power-loss-safe storage.
