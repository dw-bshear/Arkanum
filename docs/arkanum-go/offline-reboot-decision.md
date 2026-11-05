# GO-06 decision: disconnected reboot support is required

**Product decision: accepted requirement. Implementation and security validation: pending.**

The owner confirmed that field users need to reboot laptops while disconnected and continue using Arkanum Go. The earlier same-boot-only restriction is not acceptable product behavior. This supersedes that proposal in Sprint 1; it does not change what the existing proof actually does.

## Required behavior

- A normal Windows restart, shutdown/startup, or service restart must not require network contact solely because it occurred.
- An authorized user may reopen the read-only cache after restart while the original server-issued authorization remains valid.
- Keep the original signed expiration. Restart, login, app launch and service recovery never start a new 48-hour window or renew an authorization.
- Time spent shut down, restarting, sleeping or hibernating counts toward expiration. Repeated restarts must not pause or extend the offline period.
- Expired authorizations remain expired after restart; a successful server-authorized refresh is required to renew them.
- Preserve the same user/device/vault authorization boundaries, encryption, revocation state and secret-reading capability checks after restart.
- A normal restart is not itself evidence of tampering. Missing/corrupt protected state or detected clock/state rollback may still require online recovery under the reviewed security policy.

## Security design work still required

Review and implement persistent cross-boot lease state and a Windows time/clock-rollback policy that accounts for powered-off time. A boot-local monotonic counter cannot simply be continued across a new boot. Do not implement this by ignoring the old boot ID, resetting anchors, trusting a client-supplied timestamp or discarding the existing revalidation latch.

The trust model and residual risks for administrator/SYSTEM control and restored snapshots must remain explicit. No claim of tamper-proof expiry against an endpoint owner is authorized. The production time source, persistence/restore checks and cross-boot validation algorithm are not selected or proven by this requirements update.

## Acceptance additions

1. Sync a synthetic vault, disconnect, perform an actual Windows restart, sign in and read it without contacting Arkanum, before the original expiration.
2. Repeat the restart and verify the signed expiration and remaining authorization do not reset.
3. Keep the machine powered off across the original expiration; restart offline and verify reads are denied.
4. Verify service crash/restart, sleep/hibernate/Fast Startup paths, ordinary startup, expiry boundaries, clock rollback, altered lease state and restored stale state.
5. Verify an unassigned user, same-name local account and wrong device remain denied after restart.

Core policy tests, real Windows lifecycle tests and owner-network AD tests must be reported separately. A successful same-user native primitive probe does not satisfy these additions.

## Existing proof and evidence

`Arkanum.Go.Core.LeaseClock` still denies a changed boot ID. Its previous passing tests remain valid evidence for that narrower proof, **not** evidence that this requirement is implemented. Update the proof under TDD, review its cross-boot policy and exercise Windows restart behavior before closing Sprint 1. No reboot of the shared lab laptop is authorized merely by documenting this requirement; coordinate a native reboot test separately.
