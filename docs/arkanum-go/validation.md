# Arkanum Go Sprint 1 validation and domain handoff

## Status and scope

Sprint 1 is a security foundation and proof, not the Arkanum Go beta application. It must not be closed until its real domain-joined Windows acceptance criteria have passed. The approved lab Windows laptop is now domain joined; directory provenance was independently collected over authenticated, certificate-validated LDAPS. Integrated service and domain-token checks have executed, but offline Windows cached sign-in/reboot still requires the owner's console participation. An online RDP logon or an existing domain session with blocked network paths is not that test.

The proof uses synthetic fixtures only. Do not copy production vaults, site recovery/master keys, LDAP bind credentials, domain passwords or production device credentials into this test. No end-user vault viewer, production sync service, web sync API or production installer is delivered by Sprint 1.

Executed results and exact scope are recorded in [sprint-1-evidence.md](sprint-1-evidence.md), [native-acceptance.json](native-acceptance.json), and [review-status.md](review-status.md). The 131 core, 51 HTTPS proof and 266 existing web tests pass. Integrated Windows results are recorded separately; no primitive, portable or source-contract pass automatically completes the matrix below.

## Execution layers

| Layer | What it can establish | What it cannot establish |
| --- | --- | --- |
| Cross-platform core tests | Canonical AD SID parsing, signed-grant and ciphertext binding, lease-policy boundaries and negative cases with controlled inputs | Windows token provenance, IPC impersonation, service identity/ACLs, AD import or laptop time behavior |
| Standalone native primitive probe | Actual Windows account SID, native crypto operation, OS-derived named-pipe client identity, forged request identity denial and expected-server PID validation within the probe's actual execution context | A second standard user's isolation, AD identity, persistent service key ACLs, an installed sync service or logged-out refresh |
| Domain acceptance on owner network | The actual directory/laptop relationship and complete first-use/disconnected service workflow, once product adapters exist | Guaranteed secret recall, defense against local administrator/SYSTEM ownership or arbitrary snapshot restoration |

A native result must record whether the process was elevated. Success under an elevated WinRM session is not standard-user acceptance. No test should be labeled as a service test merely because a scheduled task or administrator process executed it.

## Reproducible proof commands

Use the SDK pinned by the repository's `global.json`. The Go proof is separate from the web solution and does not change the web application's version.

```sh
dotnet test Arkanum.Go.Core.Tests/Arkanum.Go.Core.Tests.csproj --logger 'console;verbosity=minimal'
python -m unittest discover -s tests -p 'test_arkanum_go_native_probe.py' -v
dotnet publish Arkanum.Go.NativeProbe/Arkanum.Go.NativeProbe.csproj -c Release -r win-x64 --self-contained true -o artifacts/arkanum-go-sprint1/native-probe
```

On Windows, run the copied, checksum-verified probe in an isolated review directory:

```powershell
.\Arkanum.Go.NativeProbe.exe --identity C:\ArkanumGoSprint1\identity.json
.\Arkanum.Go.NativeProbe.exe --self-test C:\ArkanumGoSprint1\native-result.json
```

Those commands exercise primitives, not a server connection or sync. Inspect both the process exit code and JSON results; a created file by itself is not success. Preserve exact package hashes and sanitized reports outside source-controlled code. Run once as a standard user as well as any administrator-assisted run. Do not publish real account names, SIDs, machine identities, private keys or network details in public evidence.

## Owner-network acceptance prerequisites

These prerequisites apply to the integrated proof; they are not commands implemented by the primitive probe. Use the approved synthetic lab, not a production instance or credentials.

- An approved non-production AD domain and domain-joined Windows 11 x64 laptop.
- The synthetic HTTPS proof registry populated from independently verified AD provenance; verify laptop account domain and canonical SID authority, not similar usernames. Production Web objectSid import remains a Sprint 2 adapter, not a prerequisite hidden inside this minimal proof.
- One active, secret-authorized directory user (A), a different unassigned directory user (B), an auditor-only user, and a local account with the same username as A. Provision accounts using the site's approved process, not a test script collecting passwords.
- Explicit laptop enrollment and vault assignment plus a synthetic offline-enabled vault. A's user membership is separate from the device assignment.
- The reviewed integrated proof service, protected keys/cache ACLs, authenticated local IPC, device-authenticated HTTPS server and native time adapter. These are now implemented separately from the primitive probe; see [proof-windows.md](proof-windows.md) for installation/enrollment and per-case native limits.
- Approved permission to change test account membership, disconnect the test network, lock/log out the test session and reboot the test laptop. Do not sign out a user's live lab session automatically.

## Acceptance matrix

Record test ID, exact source commit/build hash, server/client/protocol versions, execution privilege, observed result, evidence path and reviewer. `Pending` is not a passing result.

| ID | Scenario | Required result | Current state |
| --- | --- | --- | --- |
| AD-01 | Bind A from the laptop's AD directory | Store canonical objectSid and trusted directory provenance separately from existing ExternalId | Independent LDAPS provenance and proof-registry binding executed; production import adapter remains Sprint 2 |
| AD-02 | Rename A without changing SID; recreate same username with a different SID | Rename preserves the intended mapping; replacement account does not inherit old grants | Native directory-account lifecycle validation pending |
| AD-03 | Missing/duplicate/malformed SID or wrong directory domain | Fail closed without username fallback; ordinary generic LDAP behavior remains supported in web | Pending integration; pure parser tests alone are insufficient |
| AD-04 | A has never opened Go; log all users out, refresh synthetic data | Service refresh completes without user credentials, cookie, desktop process or prior Go initialization | Real virtual-service mTLS refresh with zero interactive sessions executed; production vault export is not part of this synthetic proof |
| AD-05 | Disconnect, then cached domain sign-in as A for the first Go launch | Read current synthetic password/secure note within valid authorization; no separate domain password collected by Go | Domain-token read with domain/proof paths blocked executed in an existing domain session; first-launch provenance and actual offline cached sign-in/reboot remain pending |
| AD-06 | Sign in as B, auditor-only user, and same-name local account | No unauthorized vault metadata, key material, plaintext or grant delegation | Independent unassigned-domain and same-name-local sessions executed; native auditor-only case remains pending |
| AD-07 | Forge another SID in IPC, copy cache to another profile/device, impersonate/squat pipe | Deny forged identity, cross-device use and untrusted broker before secrets leave either endpoint | Integrated forged-SID, remote SMB pipe and impostor-broker denial executed; remaining cross-device/replay/race cases need case-specific evidence |
| AD-08 | Try to read cache/device key as standard user; inspect persisted ACLs and broker identity | Only intended service identity accesses device private keys and decrypted cache; reader cannot export keys | Actual virtual identity, protected machine keys and independent standard-user cache/key-open denial executed; remaining token/effective-ACL cases pending |
| AD-09 | Remove A, disable account, remove device assignment, disable offline setting, retire vault or revoke device | Next authoritative check invalidates affected access; stale delayed responses do not resurrect grants | Synthetic HTTPS server cases executed; native signed-deny persistence and production adapter remain separate gates |
| AD-10 | No content edits; successful authorized refresh | Expiration renews despite unchanged content; no new 48-hour window from local app opening or failed sync | HTTPS proof and native refresh/service-restart/real-expiry checks executed; record the applicable run/build rather than inferred coverage |
| AD-11 | Expiry boundary, interrupted refresh, sleep/resume, service restart, wall-clock rollback, OS reboot | No failed operation extends access; expiry is enforced before reveal; verify native clock/boot behavior against the approved policy | Pending native lifecycle/elapsed-time validation |
| AD-12 | Try to mutate web vaults or use device credentials for administrative routes | All Go access remains read-only and device credentials cannot perform user/admin mutations | Real existing portal routes reject certificate-only device access in HTTPS proof tests; production sync endpoints remain downstream work |
| AD-13 | Lock/logoff/switch users with secrets displayed; clipboard subsequently changed by another app | Secret UI is remasked, prior session loses access, and clipboard cleanup does not erase unrelated new content | Installed CLI lock/disconnect denial and unlock/read executed; WPF display/clipboard remains downstream desktop implementation |

## Owner decision: reboot while disconnected must work

The owner confirmed that field users must be able to reboot and continue reading authorized offline vaults before the original expiration. Reboot, shutdown/startup, service restart and login must not start a new 48-hour period or pause elapsed time. Powered-off time counts.

The core now implements the reviewed [cross-boot clock/persistence policy](cross-boot-policy.md), and the Windows proof supplies native boot/elapsed readings. That implementation is not native reboot acceptance. Execute actual disconnected restart/repeated restart before expiry, shutdown across expiry with denied access afterward, and clock/lease-state tampering. See [decision and acceptance additions](offline-reboot-decision.md). None passes merely because a primitive/core test passed.

### Console handoff — cannot be replaced with online RDP

1. Use an approved authorized domain account with a Windows sign-in already cached, but no prior Go launch, and record that prerequisite before testing. Verify the tested build/hash, a fresh authorized synthetic grant and its exact signed expiration. Arrange the existing timed network-recovery safeguard; block the domain/proof paths while retaining approved recovery management.
2. Perform an ordinary real offline reboot, have the owner sign in at the physical Windows screen with the existing domain account, and run the standard-user CLI before expiration. Repeat a real reboot/read and verify the signed expiration and sequence did not change. Do not add autologon, persist a test password, or reconnect the domain merely to make sign-in work.
3. With a newly authorized short test lease, shut down before expiry and start after its original expiry, remaining disconnected. After the owner's cached sign-in, require no allowed read. Record actual boot/time/privilege, hash-bound committed state, CLI exit/report and absence of an intervening successful refresh; restore only the test isolation rules afterward.

This test requires the owner present at the physical sign-in screen. Disruptive-test approval alone does not establish physical participation, and remote management reachability does not establish offline cached sign-in.

## Completion rule

Attach actual results to GitHub issue #38. Keep the issue open while AD-04/AD-05 and independent unauthorized-user denial are unverified. If the owner accepts a narrower milestone for standalone proof completion, record that explicitly without rewriting unexecuted domain tests as passes. Sprint 2/production use must not silently proceed around the unresolved security gate.
