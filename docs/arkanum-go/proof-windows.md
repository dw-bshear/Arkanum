# Windows SCM / CLI integrated security proof

This is a security CLI proof, **not the final WPF product**. It builds a real Windows SCM own-process service and an interactive reader in the same executable. Windows native requirements below are **not proven** by Linux compilation, portable crypto tests, or source-contract tests. No laptop has been accessed or restarted by this implementation task.

## Build and portable verification

From the repository root (SDK .NET 10):

```sh
DOTNET="${DOTNET:-$HOME/.dotnet10/dotnet}"
uv run --with pytest python -m pytest tests/test_arkanum_go_proof_windows.py -q
$DOTNET run --project Arkanum.Go.Proof.Windows -c Release -- --self-test
$DOTNET publish Arkanum.Go.Proof.Windows -c Release -r win-x64 --self-contained true \
  -o artifacts/arkanum-go-sprint1/integrated-proof
```

The ignored `artifacts/arkanum-go-sprint1/integrated-proof` directory contains `Arkanum.Go.Proof.Windows.exe`, the .NET runtime, shared crypto/protocol assemblies, and installation/ACL/removal scripts. Ship the complete directory, not just the EXE. Public source contains synthetic IDs only. The standalone `--self-test` runs portable real-RSA/AES and lease checks; it does not initialize any Windows keys or service.

Test-first evidence: the initial Python run observed **5 failed** with the broker project absent. Separate subsequent RED runs observed missing `ExposeVerifierHandles`, missing storage-fault latch, and the missing `Broker.Transition` implementation; GREEN runs exercise the portable validation and deny-transition behavior. File-contract checks are not native runtime security tests.

## Installation (parent-owned, elevated Windows PowerShell)

Do not install or stop/restart/reboot a shared laptop without its owner's approval. All following commands are owner-run, not executed by the Linux build worker. Use an approved private test endpoint; `example.invalid` below is a deliberately nonfunctional example.

```powershell
.\install.ps1 -DeviceId 'synthetic-device-01' -Endpoint 'https://example.invalid/sprint1/sync'
Get-CimInstance Win32_Service -Filter "Name='ArkanumGoSyncProof'" |
  Select-Object Name,State,StartName,ProcessId,PathName
```

The service is automatic-start, own-process SCM, with virtual account `NT SERVICE\ArkanumGoSyncProof` and unrestricted service SID. `--service` must be launched by SCM; direct administrator execution is rejected. There is no scheduled-task substitute. Install fails on an existing service/installation directory; uninstall first for an intentional replacement. Data/keys survive normal uninstall/reinstall. Do not change a preserved installation's DeviceId without explicit purge/re-enrollment.

`acl.ps1` replaces DACLs, sets owner to Administrators, rejects reparse points, and restricts ProgramData to service/SYSTEM/Administrators. Program Files is read/execute for users and service, full control for SYSTEM/Administrators. SCM permits ordinary users only service-status query. The service grants Builtin Users process `PROCESS_QUERY_LIMITED_INFORMATION` and token `TOKEN_QUERY` only so a nonadministrator CLI can inspect the service identity; it does not grant token duplication/impersonation.

**Machine key generation is an administrative installer operation**, not a virtual-service operation. Before `Start-Service`, `install.ps1` runs the fixed-root, elevated-admin-only `--provision` command after writing config and setting ACLs. It requires an Administrators/SYSTEM-owned protected root, rejects ancestor/descendant reparse points, and validates the trusted tree **before any keystore probe**. The installer creates persistent machine CNG RSA-3072 keys `ArkanumGoSyncProof-auth` (signing) and `ArkanumGoSyncProof-unwrap` (decryption), both nonexportable, with distinct public keys, protected service/SYSTEM/Administrators DACLs and explicit Administrators ownership. It reopens and validates both before writing a create-new, administrator-owned protected **public** `bootstrap-receipt.json` binding SHA-256 of both public SPKIs and the exact config bytes. No private key is exported or transmitted. No SYSTEM service identity, machine-keystore ACL widening, or additional service privilege is needed.

The virtual service only **opens and validates existing keys**. Pristine provisioning is accepted only with both keys, no initialization marker, no established state, and the protected matching receipt. It consumes the receipt **before** writing the marker/encrypted initial state: interrupted consumption can require explicit admin recovery, never silent recreation or replay. A marker with missing state, orphan/partial keys, mismatched/unprotected receipt, or receipt beside established state fails closed. `--provision` refuses to overwrite or silently recover such stores. There is no automatic lab-recovery switch. Before initial provisioning of the previously failed proof, the parent must separately verify no real `state.bin`/authorization exists and safely preserve/reset the partial marker; provisioning itself does not delete it.

On legitimate restart/reinstall, existing key security and authenticated encrypted state are validated; original sequence, sticky deny, clock state, packages and expiration are preserved without renewal or a new receipt. Legacy missing-marker migration still writes the marker only **after** successfully authenticating existing state. A valid still-unconsumed receipt may be revalidated without replacing it or its keys. Reinstallation preserves exact existing config bytes and rejects a changed DeviceId/endpoint. Administrators/SYSTEM are trusted and can reset/delete this proof; it does not defend against local administrators or full-disk/VM rollback.

For an already-installed, stopped synthetic proof **after the parent-owned safe partial-marker reset**, deploy the complete published payload, keep `config.json` protected, and run from an elevated Windows PowerShell session:

```powershell
$exe=Join-Path $env:ProgramFiles 'ArkanumGoSyncProof\Arkanum.Go.Proof.Windows.exe'
. (Join-Path $env:ProgramFiles 'ArkanumGoSyncProof\acl.ps1')
Set-ProofAcl (Join-Path $env:ProgramData 'ArkanumGoSyncProof') $false
& $exe --provision
if ($LASTEXITCODE -ne 0) { throw 'Provisioning failed; do not start or delete state/keys' }
Start-Service ArkanumGoSyncProof
(Get-Service ArkanumGoSyncProof).WaitForStatus('Running',[TimeSpan]::FromSeconds(20))
& $exe --diagnostics
```

A nonadministrator `--provision` must fail before touching CNG. Native acceptance must read back Administrators owner, protected key DACLs, machine/nonexportable flags, distinct public fingerprints, and disappearance of the receipt after first initialization. Restart must retain the same fingerprints and all authenticated state. The protected `startup-failure.json` remains available on failed service startup and contains only stage/type/HRESULT/native error/top method/time, never exception message, identity, endpoint or content. `--diagnostics` is admin-only. Windows PowerShell 5.1 native stderr behavior still needs native execution; portable tests exercise actual harmless stderr/nonzero/missing-command children and an ErrorRecord fixture, with fresh global exit-code capture and restored error preference.

## Practical enrollment handoff

1. Allow the installer to provision keys, then start the installed service once; encrypted state initialization and public CSR creation occur under the service identity. As administrator, collect only public `%ProgramData%\ArkanumGoSyncProof\auth.csr.pem` and `unwrap.spki`. The CSR requests client-auth EKU and digital-signature key usage; private keys never leave CNG. The unwrap SPKI is a different RSA key.
2. Have the parent enrollment authority approve a **unique device mTLS certificate** for that CSR and bind the exact configured DeviceId to its certificate/public key. Never reuse a client certificate across devices. No domain password, interactive user cookie, server master key, or private PFX is required.
3. Place the issued public DER client certificate at `%ProgramData%\ArkanumGoSyncProof\client.cer`. It must match the auth key. Place the preapproved response signer DER SubjectPublicKeyInfo at `signer.spki`. The signer key may be different from the TLS key and must match the server's RSA-PSS grant/response signer.
4. Establish the HTTPS server's normal Windows trust chain through the parent's approved proof CA/root deployment. If a temporary proof root is needed, import only that public test root into LocalMachine Root after separately checking its fingerprint. Record/retain its public certificate for exact cleanup. Do not disable TLS validation. Server host name/SAN, validity, client-auth EKU, chain/revocation availability, and device mapping must be valid.
5. Ensure `config.json` in the same protected directory is **UTF-8 without BOM**:

   ```json
   {"DeviceId":"synthetic-device-01","Endpoint":"https://example.invalid/sprint1/sync"}
   ```

   The endpoint must be HTTPS with exact `/sprint1/sync` path, no credentials, query, or fragment. The installer writes this format. There is no user-writable config path or per-request enrollment override.
6. Reapply protected ACLs after copying public enrollment files from another ACL context:

   ```powershell
   . 'C:\Program Files\ArkanumGoSyncProof\acl.ps1'
   Set-ProofAcl "$env:ProgramData\ArkanumGoSyncProof" $false
   ```

7. No service restart is needed after enrollment files appear. The independent background refresh loop retries on bounded 10/20/40/60-second backoff; successful refresh schedules the next attempt after 60 seconds. HTTPS request timeout is 15 seconds and response buffering is capped at 8 MiB. Test refresh with no interactive users signed in only after owner-approved sign-out.

Normal TLS chain/host/revocation validation stays enabled; redirects and cookies are disabled. Every POST uses a new random 256-bit nonce. A response must have the exact protocol/device/nonce, increasing sequence, and RSA-PSS SHA-256 signature over `ProofWire.EncodeUnsigned`. RSA-OAEP SHA-256 unwraps only the per-package AES-256 key. The isolated core verifies signed grant identity, lease, hash, and AES-GCM with `GrantCanonical.Encode(Grant.Claims)` AAD. No synthetic protocol duplicates replace the shared types.

## Interactive CLI

Run as the actual **active, unlocked interactive user**, not a remote administrative shell:

```powershell
& 'C:\Program Files\ArkanumGoSyncProof\Arkanum.Go.Proof.Windows.exe' --status
& 'C:\Program Files\ArkanumGoSyncProof\Arkanum.Go.Proof.Windows.exe' --read "$env:TEMP\arkanum-proof-outcome.json"
```

`--read` really decrypts a matching authorized package and sends its bytes only over the local authenticated pipe. The proof CLI hashes those bytes in memory and outputs only allowed/mode/byte-count/SHA-256; it does not display or write content. `--status` also proves a currently decryptable authorized grant but returns only an allowed flag. This minimal reader selects the first successfully decryptable package for the OS user, not a multi-vault UI. An optional absolute outcome path stores that same sanitized report; do not point it into the broker directory. Exit codes: 0 authorized, 10 generic no-response denial, 1 operation/transport/server-verification error, 2 usage/non-Windows. Errors do not include account, vault, cache, endpoint, or exception metadata.

Before **any request bytes**, the CLI queries the pipe server PID, verifies it against the currently running SCM instance, opens that process/token read-only, and checks the exact virtual-account user SID and enabled service SID group. It rechecks after receiving. No expected PID or SID comes from the command line.

The native pipe is local-only (`PIPE_REJECT_REMOTE_CLIENTS`) and first-instance protected. One instance is held and reused through disconnect/reconnect, not recreated between readers. Its explicit Builtin Users access mask `0x0012019B` permits data reads/writes **without FILE_CREATE_PIPE_INSTANCE**; the client opens with that same nongeneric mask. Frames are bounded; only exact JSON `{"Command":"read"}` or `{"Command":"status"}` is accepted. Unknown fields, including a claimed SID, fail closed.

The broker impersonates the actual pipe token, requires authentication, impersonation-level token, an Interactive/RemoteInteractive (not Network) group, a nonzero token session ID, and WTS Active + unlocked session state. It derives the canonical user SID from `WindowsIdentity.User`, not a request field/name. It repeats the token/session checks immediately before response; unassigned/expired/locked/denied users receive **no response bytes or metadata**. Invalid-frame/denial paths are intentionally indistinguishable. Connection/request handling has an eight-second budget. Lock state cannot be made atomic with delivery: a lock can race after the final OS check; plaintext already delivered to an authorized process cannot be recalled. This proof does not claim protection from malware inside that user's session or DOS by repeated local pipe connections.

## Transport robustness verification

The client retries **only** native `ERROR_PIPE_BUSY` (231), with an asynchronous,
cancellation-aware 25 ms delay inside the existing ten-second whole-client budget.
Missing pipes, access denial and other failures are not retried. The production
endpoint remains fixed; access remains `0x0012019B`, and server validation still
precedes all request bytes and follows receipt before the existing bounded ACK.

Accept and disconnect share a narrow peer-closure policy: 109/232/233 only.
An early connect/close can fail `WaitForConnectionAsync` before .NET advances its
connection state. That path calls `DisconnectNamedPipe` on the **same handle**;
calling managed `Disconnect()` there would itself throw. No pipe is recreated or
name released. Unexpected accept/cleanup security or handle faults propagate to
the existing sanitized service-failure reporting rather than being treated as
peer closure. Request handling and its eight-second/lease deadlines remain bounded.

Only EOF **before the first header byte** is a generic no-response denial (exit
10). Partial headers and payloads are transport errors (exit 1, generic diagnostic
only); partial payload buffers are zeroed. A received response is still retained
until the validated client ACK; no synchronous pipe drain was introduced.

Run the executable contract harness on both platforms:

```sh
$HOME/.dotnet10/dotnet run --project Arkanum.Go.Proof.Windows/ContractTests -c Release
```

Test-first RED observed four new runtime failures (truncation classification,
missing busy retry, missing closure policy and missing reusable lifecycle), then
a separate missing accept/cleanup seam failure. GREEN executes real memory
streams (all partial-header lengths, missing/partial bodies and buffer zeroing),
controlled busy/nonbusy/canceled opens, faulted accepts/cleanup, and real generic
named pipes: connect/close **before accept**, abrupt close, partial request,
canceled request and canceled accept, each followed by a good read using the same
server object. Existing `PipeDeliveryTests` remains unchanged and exercises the
bounded ACK lifecycle. These portable results do **not** establish Windows native
accept/cleanup behavior. On Windows the same harness additionally executes real
`CreateFileW` contention with the production nongeneric access mask; Linux reports
that case as **SKIP**, never PASS. Parent-owned Windows execution of both busy and
lifecycle cases remains required.

Internal reflection helpers for the parent's native harness (not CLI options):
`Native.RetryPipeBusy<T>(Func<T>, CancellationToken)`,
`Program.HandleConnection(NamedPipeServerStream, Func<CancellationToken, Task>, CancellationToken)`,
`Program.HandleConnectionCore(Func<CancellationToken, Task>, Func<CancellationToken, Task>, Action<bool>, CancellationToken)`
(the reset callback receives whether accept completed), and
`Program.Receive(Stream, int, CancellationToken)`.
They do not authenticate test pipes or expose an alternative production endpoint.

## Persistence, clock, deny

The cache/state file `state.bin` is wholly AES-GCM encrypted, including packages, sequence, lease, and deny latch; its random local storage key is RSA-OAEP wrapped to the service unwrap key. Package content remains separately encrypted under its server-issued key. No content or unwrapped AES key is written to disk/logs. State writes use same-directory random create-new temporary files, write-through, `Flush(true)`, then `MoveFileExW(REPLACE_EXISTING|WRITE_THROUGH)`. No grant is exposed before state commit. Cache corruption fails startup rather than silently recreating a lease. A state-save error latches fail-closed in memory until service restart and successful durable recovery.

Actual Windows boot identity comes from `NtQuerySystemInformation(SystemBootEnvironmentInformation=90)` and sleep-inclusive elapsed ticks from `QueryInterruptTime`; native failure fails closed. These feed the **shared core `LeaseClock`**, not a separate Windows lease implementation. Clock decisions, including denial, are saved before any content response. Failed/untrusted sync does not renew the lease. Only a verified fresh higher-sequence complete online response may anchor trusted server time; signed grant expiration remains unchanged. Cross-boot behavior follows the sibling-owned core policy and must be separately proven with owner-approved real reboot/powered-off/sleep tests.

A correctly signed complete `Denied=true` response is **sticky**, commits even if native clock acquisition fails, and removes usable packages from the current state. Later online success does not clear it. Offline clock failure also remains latched in core state until verified online revalidation. Administrative reset is deliberately conservative: stop/uninstall with explicit `-PurgeDataAndKeys`, then re-enroll with newly approved keys/certificate. Do not erase state to troubleshoot while presenting the result as retained authorization evidence. Disk remnants/SSD secure erasure and rollback-resistant hardware counters are outside this proof.

## Uninstall and proof-only trust cleanup

Keep a copy of the scripts outside Program Files before uninstalling:

```powershell
.\uninstall.ps1                         # removes service/binaries; preserves data/keys/deny
.\uninstall.ps1 -PurgeDataAndKeys         # explicit prompted destructive admin reset
.\remove-proof-trust.ps1 -ProofRootCertificate .\approved-proof-root.cer
```

Trust cleanup compares the exact owner-selected public self-signed proof-root certificate bytes and removes **only its exact thumbprint** from LocalMachine Root, with confirmation. It does not search certificate names, delete unrelated trust, or automatically remove enterprise roots. Do not supply an enterprise/production root. If the parent imported intermediates or other proof certificates, clean up those exact separately recorded certificates using its enrollment procedure; these scripts did not install any automatically.

## Open native acceptance gates — not proven

Linux builds/portable tests do **not** prove any of these Windows/runtime requirements:

- SCM install/start/stop/recovery and actual virtual-account primary SID + service SID group; PowerShell 5.1/7 script execution; nonadministrator token-query permissions.
- Actual key ACL readback, nonexportability after reopen/restart, key access denied to unrelated users; protected file effective ACLs and crash/power-loss atomicity.
- Successful real mTLS and signer pin validation, rejected wrong certificate/host/nonce/signature/replay/wrapped key, no-user-signed-in refresh and failed-sync nonrenewal.
- Independent authorized/unassigned local and domain-user sessions; no metadata to unassigned users; request SID spoof; remote pipe denial; wrong-PID/service-token impostor; named-pipe first-instance collision and lack of user instance-creation permission.
- Lock/unlock, disconnected/RDP session handling, session change during response, service restart, actual sleep/hibernate accounting, repeated offline reboot within the original lease, powered-off expiration, rollback latch persistence, and sticky signed deny through restart.
- Verified AD objectSid/provenance and real cached domain sign-in. Those are domain integration gates, not supplied by a synthetic local test.

Observe hashes/exit codes only for synthetic payload comparisons. Never paste real SIDs, endpoints, certificates with private keys, credentials, or content into public evidence. Do not call this a completed Windows isolation proof until the native gates have actual owner-approved execution evidence.
