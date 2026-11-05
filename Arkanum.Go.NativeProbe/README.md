# Arkanum Go native primitive probe — Sprint 1

Standalone synthetic-data Windows CLI, version `1.0.0-beta.1`; no product service, accounts, passwords, or persisted keys are created. No plaintext secret, key, signature, nonce, ciphertext, or credential is written to evidence. Output does include process identity (name/SID), privilege, authenticated/forged/configured test SIDs and PIDs; handle the evidence as identity metadata. Run in an existing writable folder.

## Build and real acceptance

From the repository root (Linux cross-publish or Windows SDK 10):

```sh
dotnet build Arkanum.Go.NativeProbe/Arkanum.Go.NativeProbe.csproj -c Release
dotnet publish Arkanum.Go.NativeProbe/Arkanum.Go.NativeProbe.csproj -c Release -r win-x64 --self-contained true -o /absolute/staging/native-probe
python -m unittest discover -s tests -p test_arkanum_go_native_probe.py -v
```

On the existing Windows laptop, use the published executable (no SDK needed):

```powershell
.\Arkanum.Go.NativeProbe.exe --identity C:\ExistingWritableFolder\identity.json
.\Arkanum.Go.NativeProbe.exe --self-test C:\ExistingWritableFolder\native.json
$LASTEXITCODE
Get-Content C:\ExistingWritableFolder\native.json
```

Public options are exactly `--identity <absolute-output-JSON-path>` and `--self-test <absolute-output-JSON-path>`. Both require a fully qualified path. `--identity` records actual process identity and privilege without running primitive tests. A private `--pipe-client <random-pipe-name> <expected-server-PID>` option is only for self-spawn. Self-contained apphost spawns itself; `dotnet Probe.dll` development execution preserves the DLL argument when spawning. No remote-host changes are part of this project.

Exit 0 means the selected mode succeeded; 1 means test/evidence failure; 2 means invalid options or non-Windows; 124 means the last-resort 55-second watchdog fired (JSON may be absent). Operation cancellation begins at 45 seconds; each pipe scenario has a seven-second budget and connection retry/wait is bounded to five seconds. Child processes are terminated in cleanup; no synchronous buffered writer flush/dispose path can hang a pipe. The first-instance collision test expects Windows access-denied error 5.

The collision fixture deliberately permits two pipe instances by count so rejection exercises `FILE_FLAG_FIRST_PIPE_INSTANCE`, not the ordinary instance limit (which returned busy/error 231 in the first native run). The corrected probe passed all 11 checks under elevated and non-elevated Windows tokens; see `../docs/arkanum-go/sprint-1-evidence.md`. These are primitive passes, not service/AD acceptance.

For automated **real Windows** acceptance with Python installed:

```powershell
$env:ARKANUM_GO_NATIVE_PROBE = (Resolve-Path .\Arkanum.Go.NativeProbe.exe).Path
python -m unittest discover -s tests -p test_arkanum_go_native_probe.py -v
```

Linux source contracts are intentionally **not** native acceptance. Capture actual Windows JSON and exit status before claiming native success. Test with an ordinary interactive user where possible; an elevated WinRM run proves only that elevated process context.

## Evidence format

JSON properties: `schemaVersion` (1), `probeVersion`, `mode`, `utc`, `success`, `processIdentity`, `results`, `limitations`. Identity has `pid`, `name`, canonical `sid`, `authenticationType`, `impersonationLevel`, `elevated` (actual token elevation), `administratorRoleEnabled`, and a context disclaimer. `results` entries have `name` and `passed`; failures add exception type and HRESULT, not exception messages or secrets. Pipe successes additionally report OS-authenticated SID, deliberately forged payload SID, trusted server-configured allowed SID, authorization decision and child/server PIDs. Wrong-server-PID success reports `sentPayload: false`; it requires child rejection exit 23 and EOF with zero request bytes.

Individual self-test results:

- `windows_identity`
- `canonical_sid_round_trip`
- `cng_signature_valid`
- `cng_signature_tamper_rejected`
- `aes_gcm_round_trip`
- `aes_gcm_tamper_rejected`
- `pipe_same_caller_allowed`
- `pipe_unassigned_denied`
- `pipe_forged_sid_denied`
- `pipe_wrong_server_pid_rejected`
- `pipe_first_instance_collision_rejected`

The pipe has an explicit protected DACL granting only the actual process SID access, `FILE_FLAG_FIRST_PIPE_INSTANCE`, and `PIPE_REJECT_REMOTE_CLIENTS`. Payload is a bounded synthetic JSON frame. Server reads before `RunAsClient`, records the real Windows caller SID and authorizes **only** against its trusted assignment, never the claimed SID. Even the allowed case carries a forged SID. Unassigned and forged-to-assigned-identity cases are denied. The child verifies the actual server PID using `GetNamedPipeServerProcessId` before sending any bytes. The wrong-PID case uses expected PID zero deliberately.

## Deliberate limitations

Same-user self-spawn, including elevated WinRM, is **not cross-user isolation**. This is not proof of AD mapping, a production service SID, CNG persisted key ACLs, logged-out refresh, or lease/sleep/reboot behavior. Those remain later manual/domain/service acceptance. No production security claim is made. This probe neither creates local accounts nor installs persistent services. RSA CNG keys are unnamed ephemeral keys, destroyed on disposal.

## Primary Microsoft references

- CreateNamedPipeW: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea
- RunAsClient: https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstream.runasclient
- Named-pipe impersonation: https://learn.microsoft.com/en-us/windows/win32/ipc/impersonating-a-named-pipe-client
- GetNamedPipeServerProcessId: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid
- CngKey.Create (null key name is ephemeral): https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cngkey.create
