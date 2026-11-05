# Cross-boot lease clock policy — Sprint 1 core proof

## Scope and product rule

This implements the owner's GO-06 requirement in the cross-platform policy core: ordinary offline boot changes do not themselves deny access. The original signed grant expiry is immutable. Reboot, service restart, login, unlock and read do not renew authorization. `LeaseClock` decides clock validity and supplies effective UTC; the existing `SnapshotProofReader` separately denies at or beyond signed expiry and rejects grants exceeding 48 hours. A clock decision can therefore be allowed while the expired grant is denied.

This document supersedes the **core algorithm status** statements in the older architecture/protocol/reboot-decision documents; it does not close their production-adapter, security-review or Windows acceptance gates. Those files were outside this change's ownership.

## State and compatibility

Existing constructor calls remain valid. `LeaseState` appends optional `DateTimeOffset? EffectiveHighWaterUtc = null` after the existing optional revalidation latch. Existing public type and method names remain unchanged. Consumers persisting the state must preserve the appended field; no new wire protocol or persistence adapter is implemented here.

* `HighWaterUtc`: maximum **observed wall UTC** only. Never write monotonic-derived UTC into this field. Rollback comparisons use this value, not effective time.
* `HighWaterElapsed`: maximum accepted elapsed reading for the current boot (also advanced on a same-boot denied reading when possible).
* `EffectiveHighWaterUtc`: durable effective UTC floor, which cannot decrease on accepted advances.
* `BootId`, `AnchorUtc`, `AnchorElapsed`: current boot identity and conservative effective-time anchor. A boot transition changes these only after validation. New boot `AnchorUtc` may legitimately exceed observed wall UTC.
* `NeedsOnlineRevalidation`: sticky fault latch; reboot and later plausible readings never clear it.

For trusted legacy state with no effective field, reconstruct the floor as `max(HighWaterUtc, AnchorUtc + HighWaterElapsed - AnchorElapsed)`. Validate the legacy wall/anchor ordering first. This is compatibility initialization, not renewal. Missing/unreadable state is **not** an instruction to construct a fresh lease. The broker must reject missing state; only authenticated authorized online validation can establish replacement state. A production versioned state envelope should distinguish legacy records from truncated or downgraded modern records; the nullable compatibility field alone does not authenticate a schema version.

## Advance algorithm

Let `F` be the validated old effective floor, `W` the old observed-wall high-water and `E` the old boot's elapsed high-water. Read actual broker-owned OS boot identity, wall UTC `w` and sleep-inclusive boot elapsed `e`.

1. Deny and latch invalid state, missing/blank boot IDs, negative elapsed values, arithmetic/date overflow, wall rollback (`w < W`), or same-boot elapsed rollback (`e < E`). Persist denials; do not repair/reinitialize state offline. Explicit effective state must be at least the observed wall and the anchor-derived monotonic floor. Existing latch always denies.
2. **Same boot:** `Fnew = max(w, F + (e - E))`. Anchors remain unchanged. This consumes elapsed time even after a wall forward jump followed by a frozen wall.
3. **Changed boot:** `Fnew = max(w, F + max(w - W, e))`. The wall delta accounts for shutdown/startup time under the trusted-RTC assumption. New boot uptime is an independent conservative lower bound, not continuity of the old boot counter. Add the larger delta to the *old effective floor*, not to the old wall time. Set the new anchor to `(Fnew, e)` and retain `Fnew` durably.
4. Set observed-wall high-water to `max(W, w)` and current-boot elapsed high-water to `e`. Successful advances never change a signed grant, expiry, authorization generation or deny state.

This is deliberately conservative: a monotonic-derived lead over wall UTC is retained. A later wall catch-up or overlap with new boot uptime may be charged conservatively and expire a grant early, never used to subtract accrued time. New boot uptime can include time before the previous observation in unusual clock/identity lifecycle paths; conservative overcharging is preferable to renewal. Native clock/boot identity correctness remains required.

Repeated serialized boots with a frozen/slow nondecreasing wall retain all already observed monotonic progress. They do not reset the floor or convert effective time into observed-wall rollback evidence. An actual backward observed wall jump remains a fault even on a new boot. Ordinary service restart with the same persisted state and unchanged boot/counter adds no time and grants no new interval.

## Trust model and residual limits

**RTC/wall time, the OS boot identity and sleep-inclusive elapsed source, and broker-only persisted state are trusted relative to standard users, not hostile administrators.** Reading values from an untrusted client or permitting standard users to alter the service state/time configuration invalidates this model.

Powered-off time is accounted by the advancing RTC/wall source. A boot-local timer cannot measure an interval while the machine is off. Frozen/slow wall tests establish that *observed running time* is not erased across boots; they do **not** prove detection or measurement of invisible powered-off time with a frozen/slow RTC. Nor can this pure function charge running time after the last durable observation but before power loss when neither a trustworthy RTC delta nor another trusted counter reveals it. There is no software-only promise of strict real-time 48-hour enforcement under those conditions.

Coordinated administrator/SYSTEM modification of RTC, boot readings and persisted floors/latches; a restored disk/VM snapshot; privileged key recovery; or a compromised service/kernel can replay apparently consistent state or bypass this code. A forward wall jump can conservatively cause early expiry; correcting it backward then requires online recovery. File ACLs, signatures and software state do not provide a hardware anti-rollback clock or defeat a privileged endpoint owner. Stronger guarantees require an independently trusted time/monotonic authority or online contact and a reviewed recovery design. No cloud service or hardware attestation is claimed.

Production broker obligations (not implemented by this pure clock):

* Service-controlled ACLs and integrity/confidentiality protection for complete state outside replaceable snapshot directories; no reader-supplied clocks or anchors.
* Serialize clock/policy decisions and atomically persist **all** `NextState` fields before any plaintext release, including denied results. Persist failure means no data. Retain latches on crash/restart; no fallback to older snapshots or five-field serialization.
* Establish initial anchors only at authenticated online authorization; bind to response freshness and all grant/certificate/trust caps. A valid clock never overrides identity, scope, signature, known deny or expiry checks.
* Verify native sleep-inclusive elapsed source and stable boot identity through actual Windows reboot, sleep, hibernate, Fast Startup, service crash and restart. Reboot tests here use deterministic readings and JSON round trips, not real Windows lifecycle evidence.

## Verification evidence

Baseline command (SDK `$HOME/.dotnet10/dotnet`, `DOTNET_ROOT=$HOME/.dotnet10`, SDK prefix on `PATH`):

```text
dotnet test Arkanum.Go.Core.Tests/Arkanum.Go.Core.Tests.csproj --no-restore -v minimal
Passed!  - Failed:     0, Passed:   106, Skipped:     0, Total:   106, Duration: 1 s - Arkanum.Go.Core.Tests.dll (net10.0)
```

Tests were added/updated before the production change. The first real RED compiled and failed on ordinary/repeated boot permission, powered-off boot permission, chained frozen/slow-wall boot permission, monotonic rollback setup after a permitted boot, and forward-jump/frozen-wall effective time:

```text
Failed!  - Failed:     9, Passed:   115, Skipped:     0, Total:   124, Duration: 1 s - Arkanum.Go.Core.Tests.dll (net10.0)
```

Existing wall/elapsed rollback, corrupt state, overflow and latch tests remained regression coverage; new malformed-state/reading cases also already failed closed under the old implementation. No false claim that every new regression test was initially red is made. An initial post-implementation run reported 123 passed / 1 failed because `SnapshotProofTests.Boot_change_denies_a_valid_signed_snapshot` encoded the superseded owner requirement; only that lease assertion was changed to permit the valid snapshot. The resulting first GREEN was:

```text
Passed!  - Failed:     0, Passed:   124, Skipped:     0, Total:   124, Duration: 1 s - Arkanum.Go.Core.Tests.dll (net10.0)
```

Additional regression coverage checks serialized separate durable/observed floors, invalid effective floors, cross-boot overflow and real signed/encrypted snapshot expiry at/after 48 hours with unchanged signed grant bytes. These tests add coverage after the feature GREEN rather than additional production behavior. Final verification with the same command returned:

```text
Passed!  - Failed:     0, Passed:   131, Skipped:     0, Total:   131, Duration: 1 s - Arkanum.Go.Core.Tests.dll (net10.0)
```

Native Windows and owner-network AD acceptance remain pending; Sprint 1 is not declared complete by these core tests.
