# Arkanum Go Sprint 1 — isolated security core proof

**Cross-platform proof only, not a production broker or server API.** These projects do not reference or change the production web application, existing solution, identity models, or existing `ExternalId` values. Both target .NET 10 with explicit `1.0.0-beta.1` / assembly-file `1.0.0.0` versions. No new runtime dependencies. Signing and encryption fixture helpers live only in the test project.

## Small review surface

- `AdSid`: canonical AD domain SID (`S-1-5-21-a-b-c`), user SID (`...-rid`) and exact 28-byte user `objectSid` decoder. The 48-bit authority is big-endian; the five 32-bit subauthorities are little-endian. Only revision 1, authority 5, domain prefix 21 and exactly three domain components plus RID are accepted. Decimal components must be canonical ASCII uint32: no sign, leading zeros, whitespace, overflow, extra/truncated data, or domain mismatch. Returns a SID string; performs no identity migration and never reads/writes `ExternalId`.
- `LeaseClock.Advance`: pure decision over abstract UTC/boot-ID/elapsed readings and trusted persisted state. No platform clock calls.
- `SnapshotProofReader.Open`: advances the clock **before** grant processing, then returns either authenticated plaintext or `null`, plus `NextState`. Constructor pins a trusted RSA public SubjectPublicKeyInfo (minimum 2048 bits); grants cannot supply a signer key. No caller signature-valid flag exists. A private `VerifiedGrant` capability separates signature/claim verification from decryption.

## Cryptographic contract

RSA-PSS/SHA-256 signs the fixed canonical encoding of **every** grant field: schema and schema version, grant ID, device ID, vault ID, AD user and domain SIDs, content version, SHA-256 plaintext content hash, issued-at, expires-at, revision, deny watermark and explicit deny flag. Current schema is `arkanum-go-offline-grant`, version `1`.

Canonical bytes use ordered `BinaryWriter` strings (7-bit encoded UTF-8 byte lengths, strict UTF-8), little-endian int32 schema version, int64 content version / UTC .NET timestamp ticks / revision / deny watermark, then a one-byte boolean, in the declaration order of `Grant`. Timezone offsets normalize to UTC. This is an explicit proof format, **not** a claim that arbitrary JSON can be signed canonically. There is no untrusted JSON/parser/network layer here.

An AES-256-GCM snapshot has a 12-byte nonce, ciphertext and 16-byte tag. All canonical grant bytes are GCM additional authenticated data. Decryption also verifies SHA-256 of the resulting payload against the signed hash using a fixed-time comparison. Failed plaintext buffers are zeroed; successfully returned buffers belong to the caller. Test fixtures encrypt the entire document, including titles, folder names and secrets, together. Envelope IDs, SIDs, hash, version and lease/deny fields are not confidential: the proof does **not** conceal grant authorization metadata or ciphertext length.

The reader requires exact user SID/device/vault/content-version matches, nonnegative revision/deny floors, grant revision and deny watermark at least those trusted floors, and no explicit deny. Expiry must be strictly later than the effective current time, issue must not be in the future, and the issue-to-expiry interval is positive and at most 48 hours. Equal expiry denies, with no clock-skew grace.

## Trusted adapter obligations (not implemented)

`OpenContext.UserSid` must come from **authenticated Windows named-pipe client impersonation / OS token SID**, not a username, client-provided SID, request header or cached claim. The configured device ID, selected authorized vault/content version, revision floor and deny floor are trusted broker inputs too. The adapter must obtain trustworthy boot ID / elapsed readings and durable, integrity-protected `LeaseState`. These abstract inputs are NOT proof of OS authentication or secure persistence.

The broker must serialize requests and durably persist `NextState` on **success and failure**, before exposing any returned plaintext. Failure to persist must deny and discard/zero plaintext. Never recreate anchors on open, parse error, bad signature or authentication failure. Protect against persisted-state rollback; this pure evaluator cannot detect restoring the entire state plus its trusted inputs. Input DTO arrays must not be concurrently mutated; transport bounds and concurrency/lifecycle controls belong to the adapter. Keys, state and floors must never be accepted from an ordinary client. The broker retains the AES key/device private key; this proof neither creates nor exports device private keys.

Within a boot, effective UTC consumes newly observed sleep-inclusive elapsed time on top of the persisted effective floor. Observed wall UTC is tracked separately; a slow/frozen wall must not erase monotonic progress or be mistaken for rollback merely because effective time runs ahead. On an ordinary boot change, the core conservatively consumes the greater of observed wall progress and new-boot uptime, retains the effective floor, and changes only the boot-local anchor—not signed grant expiration. UTC/elapsed regression or corrupt state latches `NeedsOnlineRevalidation`; apparently valid later readings do not silently recover it. See [cross-boot policy](../docs/arkanum-go/cross-boot-policy.md) for the algorithm and residual RTC/admin/SYSTEM/snapshot limits. Cross-platform tests cover these decisions, but actual native reboot/shutdown/sleep acceptance remains a separate gate. Revision/deny floors must come from durable trusted online sync; disconnected clients cannot discover an unseen revocation before their existing lease expires.

No logging, network service, hourly scheduler, Windows service, UI or cache file persistence is implemented. Native pipe ACLs/impersonation, file ACLs, machine-only key protection (DPAPI/CNG), key rotation, atomic anti-rollback cache/state persistence, service operation with no logged-in users and hourly sync still require a Windows integration implementation and real native tests. **The full native Windows security gate remains OPEN.** Linux unit tests do not close it.

## Run independently of the existing solution

```sh
dotnet test Arkanum.Go.Core.Tests/Arkanum.Go.Core.Tests.csproj
dotnet restore Arkanum.Go.Core.Tests/Arkanum.Go.Core.Tests.csproj --locked-mode
dotnet test Arkanum.Go.Core.Tests/Arkanum.Go.Core.Tests.csproj -c Release --no-restore -warnaserror
```

See `../Arkanum.Go.Core.Tests/TDD-EVIDENCE.md` for observed RED/GREEN results.
