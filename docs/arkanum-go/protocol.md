# Arkanum Go — proposed Sprint 1 protocol and state contract

**Status: proposed production contract, not the production web API.** A narrower synthetic HTTPS server and Windows SCM/CLI proof now exist; see [proof-server.md](proof-server.md), [proof-windows.md](proof-windows.md), and [executed evidence](sprint-1-evidence.md). Read [architecture.md](architecture.md) for trust boundaries and ADR approval gates. Field names, algorithms and limits below remain the proposed production contract; differences must be reconciled explicitly, not described as implemented. Sprint 2 will select server version/API compatibility. No cloud dependency.

## Implemented proof versus proposed production contract

`Arkanum.Go.Core` deliberately exercises a smaller, isolated cryptographic boundary. It is not a serializer or client for the proposed production wire contract below. Do not send its object model as a production API or claim protocol interoperability from its test results.

| Area | Implemented proof | Proposed production contract / remaining work |
| --- | --- | --- |
| Canonical encoding | Ordered length-prefixed binary strings and integers; UTC .NET ticks | Strict JSON/JCS, domain-separated signed messages and interoperability vectors; no JCS implementation currently exists |
| Signing key | Pinned RSA SPKI, minimum 2048 bits, RSA-PSS/SHA-256 | Minimum 3072-bit keys, trust/key identifiers, rotation and certificate/validity caps |
| Snapshot binding | Hash of authenticated plaintext; entire signed grant is AEAD AAD | Shared device/vault snapshot bound by encrypted-envelope digest and separate authenticated header |
| Renewal | Changed grant bytes require fresh AEAD encryption in the fixture | Unchanged shared snapshot can receive new per-user authorizations without rewriting ciphertext |
| Identity and scope | Canonical user/domain SID, device/vault/content version and caller-supplied trusted revision/deny floors | Verified site/directory provenance, membership/effective-role calculation, device/key generations and registry checks |
| Denial and clock | Signed deny, encrypted durable sequence/deny/lease state, reviewed conservative cross-boot core policy, native Windows boot/elapsed adapter | Scoped deny supersession and full native lifecycle qualification; hardware rollback resistance is not claimed |
| Platform | Separate synthetic HTTPS/mTLS server and real Windows virtual-account SCM service, protected CNG keys, encrypted cache and local authenticated CLI IPC | Production web integration, WPF reader and beta installer remain downstream work; native acceptance is case-specific, not implied by builds |

The separate native primitive probe is also not the production broker: same-user child-process tests do not establish service-specific key ACLs, cross-user isolation or domain authentication. See [validation.md](validation.md) for the evidence layers and manual domain handoff.

## 1. Common encoding and cryptographic profile

* Protocol identifier `arkanum-go/1`; separate Go product version `1.0.0-beta.1`. Version negotiation never relaxes authentication or permits unknown security-critical fields/algorithms. Reject unsupported major versions.
* UTF-8 JSON with a strict versioned schema. Reject duplicate properties, malformed Unicode, unexpected fields, out-of-range numbers, invalid IDs/SIDs, trailing data and excessive nesting (maximum 16). IDs are opaque bounded strings (maximum 128 UTF-8 bytes); directory GUIDs and canonical SIDs have explicit typed validators, not permissive coercion. No request string is a file path.
* Proposed canonicalization: RFC 8785 JSON Canonicalization Scheme over the `payload` object; signed integers must be exactly representable, and monotonic sequence/generation counters are decimal digit **strings**, validated and compared numerically without truncation or wrap. UTC instants are integer Unix seconds. Binary uses unpadded base64url. No lossy Unicode normalization of identifiers.
* Envelope `{payload, signature}`; `payload` includes `protocol`, `messageType`, `siteId`, `keyId` and all semantic fields. Signature is domain-separated: ASCII `ArkanumGo/v1/` followed by message type, a zero byte, then canonical payload bytes. Proposed signature suite RSA-PSS/SHA-256 with MGF1-SHA-256, 32-byte salt and RSA keys at least 3072 bits. Reject algorithm substitution and wrong context. Key algorithm is installed trust configuration, not attacker-selected JSON.
* Snapshot cipher: AES-256-GCM, fresh 256-bit DEK and fresh 96-bit nonce per snapshot; 128-bit tag. Never reuse a DEK/nonce pair. Wrap DEK using registered device unwrap public key with RSA-OAEP/SHA-256 (MGF1-SHA-256, empty label; RSA at least 3072 bits). Device-authentication key is a **different** nonexportable CNG key; it must not unwrap payloads. These choices are proposed interoperable fixtures, not compliance certification.
* AAD is canonical snapshot header excluding ciphertext, tag and hash fields; include protocol/type/site/device/deviceGeneration/vault/snapshotId/snapshotGeneration/policyEpoch/unwrapKeyId/nonce/wrappedDek and algorithms. Compute SHA-256 over the entire canonical encrypted snapshot envelope; grants bind that digest. A signature authenticates authorization, while AEAD authenticates encrypted payload and header. No unauthenticated parsing may return metadata.
* Per-device/per-vault snapshot contains only **current** credentials and secure notes: encrypted vault/folder names, titles, usernames, secret values and note bodies. No password history, deleted items, attachment bodies, server master/recovery keys or plaintext search index. Outer opaque IDs, lengths and routing information are not claimed secret. Do not log plaintext decrypted JSON.

## 2. Enrollment independent of an interactive user

Proposed endpoint names are descriptive placeholders, not existing routes.

1. An authorized web administrator approves an enrollment intent at the configured on-premises site. Intent includes `siteId`, `enrollmentId`, expiry, one-use nonce, allowed device instance and separate authentication/unwrap public-key fingerprints. Approval is distinct from vault/device or vault/user assignment.
2. Elevated installation provisions distinct nonexportable machine authentication and unwrap keys with service-specific CNG key ACLs; the service opens and validates existing keys rather than requiring machine-key creation privilege. Device submits a CSR plus both public keys and `enrollmentId`, protocol/version, nonce and proof over all bindings. Never accept a subject/CN as authorization. No UI login, user cookie, user password, domain machine password or user-profile DPAPI is required. The proof's one-use local provisioning receipt is described separately in [proof-windows.md](proof-windows.md).
3. Server verifies approval, nonce expiry/single use and authentication-key signature; prove unwrap-key possession via a fresh encrypted challenge and response bound to enrollment transcript. Fail incomplete possession proof; approval alone is not key possession. Atomic nonce consumption prevents competing enrollment replay. No hardware attestation claim.
4. Server issues client-authentication certificate and signed enrollment receipt: `siteId`, `deviceId`, `deviceGeneration`, both key IDs/fingerprints, certificate issuer/serial/fingerprint, `issuedAt`, `notAfter`, policy/trust versions and enrollment transcript digest. Certificate identity maps to the server enrollment record, not arbitrary certificate text. The service verifies receipt against already installed site signing trust.
5. Registry state begins with no vault entitlement. Device and user assignments are performed separately in web controls. Approved enrollment credentials travel through protected setup, never logs or source control. Enrollment cancellation/expiry fails closed.

Certificate renewal uses existing private-key proof and explicit server approval of new certificate/key binding. Unwrap rotation creates a new key generation and freshly wrapped snapshots; never silently repurpose the authentication key. Device reset/reinstallation creates a new device generation; old grants cannot carry forward.

## 3. Device sync transport and complete response

TLS 1.2 or newer, normal hostname/chain/EKU/validity verification, configured private-enterprise CA as appropriate, client certificate authentication plus enrolled-key binding. No `TrustServerCertificate`, certificate-validation bypass or automatic redirects to another origin. Service network permissions need only configured server and enterprise PKI endpoints; no blanket domain credential use. Certificate revocation is checked on authorized online validation using configured enterprise CRL/OCSP/registry policy. Unavailable or stale required revocation evidence cannot create/extend a lease. A network failure is not an authoritative revoke by itself.

`SyncRequest` fields:

| Field | Meaning |
| --- | --- |
| `protocol`, `siteId`, `deviceId`, `deviceGeneration` | Exact enrolled identity; mTLS registry binding must match. |
| `requestId`, `nonce` | Unique bounded request ID and fresh cryptographic 256-bit nonce; server response binds both. |
| `authKeyId`, `unwrapKeyId`, `certificateFingerprint` | Current enrolled key/cert generations; no user identity field can expand scope. |
| `lastCommitId`, `lastResponseSequence`, `denyWatermarks`, `snapshotGenerations`, `trustBundleVersion` | Hints for synchronization, never server authority. Server computes full effective policy from current data. |
| `clientVersion`, `supportedProtocolVersions` | Compatibility checks; no insecure downgrade. |

Signed `SyncManifest` fields: common signed header; `requestId`, `nonce`, `deviceId`, `deviceGeneration`, strictly increasing `responseSequence`, `commitId`, `serverTime`, `issuedAt`, `notAfter`, `policyEpoch`, `trustBundleVersion`, `certificateFingerprint`, `authKeyId`, `unwrapKeyId`, complete snapshot descriptor list (`vaultId`, generation, ID, encrypted-envelope SHA-256, byte length), complete signed grant list/digests, and applicable signed deny records/watermarks. Include `complete: true` and explicit `scope: device`; paginated/partial data must stage to a final complete manifest before becoming readable. No “absence means unchanged” ambiguity.

Each refresh represents authoritative state for the enrolled device, including eligible users who have never used this laptop. User eligibility comes from explicit assignments and verified directory mappings, not discovery of profiles or a user SID sent by the service. The server must recheck effective `CanReveal`, account/directory status, offline switch and assignments at issuance; exporting inventory permissions as reveal permission is forbidden.

A complete newer manifest omitting a previous grant removes it at commit; partial/failed refresh never deletes by omission or renews anything. Explicit authenticated deny is processed immediately as described below, even if the remaining snapshot download fails.

### Signed grant

One `VaultSidGrant` per `(siteId, deviceId, deviceGeneration, vaultId, subjectSid)`:

| Fields | Required binding |
| --- | --- |
| `grantId`, `grantGeneration`, `policyEpoch`, `responseSequence` | Unique authorization and monotonic policy/version context. |
| `appUserKey`, `subjectSid`, `directoryId`, `directoryType`, `authorityId`, `objectGuid`, `mappingVersion` | Verified AD account/provenance; `directoryType` must be the approved AD mapping type. No DN/username/email join or SIDHistory fallback. |
| `deviceId`, `deviceGeneration`, `authKeyId`, `unwrapKeyId`, `certificateFingerprint`, `vaultId` | This device/key/cert and this vault only. |
| `canReveal: true`, `membershipVersion`, `deviceAssignmentId`, `deviceAssignmentVersion` | Effective reveal capability, existing explicit user-to-vault membership, and separate device-to-vault assignment; membership alone does not authorize device distribution. No new per-user offline switch is proposed. |
| `snapshotId`, `snapshotGeneration`, `snapshotDigest` | Exact authenticated encrypted bytes; no grant applied to another or newer payload. |
| `validatedAt`, `notBefore`, `expiresAt`, `maxOfflineSeconds` | Last authorized server validation, activation and hard expiry. Proposed maximum `maxOfflineSeconds` is 172800. Expiry never slides at local login. |
| `certificateNotAfter`, `revocationEvidenceValidUntil`, `signingTrustValidUntil` | Signed validity caps; do not rely on disconnected live revocation discovery. |

Audit-only accounts receive no reveal grant. For mixed roles use actual server effective capability as documented in architecture, not a blanket “contains Auditor” check. Local service authenticates caller token's user SID and checks the exact signed binding; requested SID/UPN cannot substitute for token identity.

### Signed deny

`DenyRecord` includes common signed header, `denyId`, `scope` (`device`, `vault`, `subject`, or `device-vault-subject`), exact scoped identifiers, `policyEpoch`, `denySequence`, `effectiveAt`, machine-readable `reason` and affected generation cutoffs. Reasons include device revoked, cert revoked, account disabled/missing, mapping changed, offline disabled, device unassigned, user unassigned, reveal capability removed and vault retired. Authenticate and validate scope before acting; unsigned HTTP error text is not a signed policy update.

## 4. Invalidation precedence and stale-response suppression

All read decisions and commits share a serialized policy-state boundary. Capture state generation for each operation; before any response recheck generation, deny state, token/session and time.

1. **Local authentication/session failure** denies that call before metadata and cancels associated pending reads. Service identity validation failure prevents reader use.
2. **Known device/certificate/trust revocation or authenticated deny** latches the applicable denial durably, increments local invalidation generation and cancels affected in-flight reads. Key/trust revocation can invalidate several vaults. It overrides an older still-unexpired grant.
3. **Untrusted local state/clock, expired certificate/trust/revocation evidence/grant, binding/signature/AEAD failure** denies affected reads. A normal boot change alone must not deny an otherwise valid offline lease; the core policy is implemented, with real cross-boot acceptance still pending. Corrupt global policy state denies all reads. No older-file fallback.
4. **Allow** is considered only after all earlier gates. Match exact manifest/grant/snapshot and both assignments; no usable grant is itself denial.

Maintain highest accepted response sequence per device generation, policy epoch and scoped deny watermarks in protected durable state outside replaceable snapshot directories. A response started before a deny or accepted newer policy cannot commit/release data after it. Reject lower/equal superseded response sequences, grant/snapshot generation regressions, wrong nonce/commit/digest and any policy epoch below high-water. Equal sequence is allowed only as byte-identical idempotent recovery of the same already committed transaction, never as renewal.

A newer allow does **not** erase a deny by mere arrival. Recovery requires fresh authorized online revalidation with a signed explicit `DenySupersession` naming the relevant deny IDs/watermarks, matching scope, higher policy epoch/sequence and new active assignments/key generation where required. Persist supersession with new grants atomically. A deny and allow at identical ordering coordinates resolve to deny. Race tests must cover late success after disable, retry after revoke, stale disk restoration and crash while persisting denial.

Disconnected clients cannot learn new server changes instantly. Old authorized access may last only to the earliest remaining lease/validity cap unless the deny is received earlier. Local deletion and server revocation cannot recall copied secrets or defeat privileged disk/VM snapshot restoration; no absolute erase claim.

## 5. Cross-boot lease requirement and pending clock design

**Owner requirement confirmed:** ordinary offline reboot/shutdown/startup must preserve access within the original signed authorization period. The same-boot-only proposal is superseded; see [GO-06 decision](offline-reboot-decision.md). The current core implements the reviewed [cross-boot policy](cross-boot-policy.md); this is not a substitute for native reboot/cached-sign-in acceptance.

At authorized validation commit capture actual OS boot identity, sleep-inclusive monotonic tick/frequency, local wall UTC, authenticated `serverTime` and response round-trip timing. Signatures alone do not make stale server time current: bind nonce/request, enforce response timeout and conservatively subtract full request elapsed time from available lease. Persist these anchors plus wall-clock high-water, cert/trust caps, grant validation time and remaining interval. No refresh may extend `expiresAt` or `maxOfflineSeconds` without revalidating that grant.

For each read, verify protected lease/deny state and the approved time continuity checks before releasing data. Preserve original signed expiry and validity caps across boots. Time spent powered off counts; repeated restarts cannot extend or pause the window. Same-boot monotonic regression remains a fault, but a new boot's elapsed counter cannot be compared as though it continued the previous boot. Effective expiry remains the earliest of grant expiry, validated-at plus permitted offline duration, certificate expiry, revocation-evidence validity and signing-trust validity. Boundary at expiry is denied.

Missing/corrupt protected state or detected rollback may enter `NeedsOnlineRevalidation`; ordinary reboot by itself must not. The core and Windows adapter now implement conservative boot transitions, native boot/elapsed inputs and encrypted persistence. Powered-off accounting trusts RTC progress, not a hardware anti-rollback counter. Authenticated online refresh remains the only renewal/recovery path where recovery is required. Remaining native tests include repeated offline reboot/cached sign-in, shutdown across expiration, sleep/hibernate/Fast Startup and clock/state rollback.

## 6. Atomic cache transaction and local state

Cache root, key handles, trust config, journal, watermarks and staging areas are service-controlled with explicit ACLs; readers never open them. Store policy/identity fields containing SIDs and mappings encrypted at rest as part of service-protected state. No plaintext titles/usernames/notes in manifest, filenames, logs or crash diagnostics.

1. Stage complete encrypted response under a fresh transaction ID on the same volume. Bound all allocations before decoding. Verify trusted signing chain/bundle, request binding, times, certificate registry state, policy high-water, every grant and every descriptor/hash; validate AEAD privately without exposing data. Clear temporary plaintext best effort.
2. Apply authenticated denies durably first. A failed/partial allow fetch must not undo them. Do not treat a TLS success or incomplete response as lease renewal.
3. Flush encrypted staging files and journal, then recheck invalidation generation under the policy commit lock. Commit one manifest pointer/transaction containing the complete grants/snapshots/anchors and monotonic acceptance state. Use verified Windows same-volume atomic replace plus crash-recovery journal; exact flush/replace implementation is a native proof gate, not asserted durability from rename alone.
4. Read visibility changes only after successful durable commit. No mixed generation, grant without matching snapshot, or staged plaintext. On uncertain recovery, deny pending authorized revalidation. Replay protection state must not roll back with a cache directory replacement.
5. Remove superseded encrypted files/key references best effort after commit; keep no user-readable archives. Loss of anti-rollback state fails closed. Software protected state is not a trusted hardware monotonic counter against administrator/SYSTEM snapshots.

## 7. Local reader protocol (not yet a Windows adapter)

Pipe `\\.\pipe\ArkanumGoSync.v1`; no TCP listener or remote pipe access. Explicit native remote-client rejection, scoped DACL, service-only create-instance permission and WPF server-identity validation are mandatory as described in architecture. A cross-platform in-process broker test is not this IPC implementation.

Before sending sensitive application requests WPF validates pipe server PID/process handle against SCM service instance and virtual-account/service SID; restart/race/inspection failure closes the connection. The service derives token user SID and logon/session identity via successful Windows pipe token inspection; validates unlocked interactive local session and rechecks before response. Do not trust a JSON SID, username, PID or session ID. No unauthenticated “list cache”/vault count/metadata response; only generic unavailable/denied status before authorization.

Length-prefixed frames: unsigned 32-bit little-endian byte count followed by exactly that many UTF-8 JSON bytes. Proposed operations `Hello`, `ListVaults`, `ListItems`, `ReadItem`, `Close`. `Hello` contains protocol/client version/request ID only and creates no lease. Requests carry request ID and opaque vault/item IDs, never caller identity or filesystem paths. Responses carry request ID, result/error and current connection generation. List operations apply the **same** grant/time/session gates as secret reads. No names, usernames, counts or existence-specific errors for unauthorized scope. Bind every request to the authenticated connection; reject duplicate outstanding IDs. No offline mutation operations.

Use a per-connection cancellation generation: lock/logoff/disconnect, service restart, deny, expiry or policy change cancels pending work. Recheck just before sending each frame and never release cached response from a previous identity/generation. OS lock may race already delivered bytes; WPF must promptly clear/mask on notification and never promise recall of data already delivered. Reader display masking and owned-clipboard best-effort cleanup are separate from lease expiry. Plaintext stays only in service/authorized-reader memory as needed; no disk temp, UI persistence, preauth index or secret-bearing logs.

### Proposed resource and timeout limits

These are reviewable beta limits, not observed production capacities. Reject over-limit data; do not truncate signed state or extend leases on failed refresh.

| Resource | Limit / failure behavior |
| --- | --- |
| Enrollment challenge | 5-minute validity, single use, bounded attempts/rate limit per approved intent; fail without enrollment. |
| HTTPS | 10-second connect/handshake, 60-second total request; abort on deadline. No cross-origin redirects. |
| Control/manifest | Maximum 4 MiB decoded bytes, maximum 4096 grants/denies/descriptors each. Snapshot downloads separately bound. |
| Snapshot | Maximum 32 MiB encrypted envelope per vault; maximum 256 MiB staged batch/device. No compression in beta; no archive expansion. |
| Snapshot content | Maximum 10000 items/vault, 1 MiB secret/note field, 4096-byte titles/usernames; enforce total snapshot cap as well. |
| Pipe | Maximum 8 MiB/frame; preauth/Hello request maximum 4096 bytes. Reject zero/oversize length before allocation. Page authorized list results within this bound; `ReadItem` returns one item, never the whole snapshot. |
| Pipe timing | 5-second connect/Hello/frame deadline, 10-second operation deadline, 60-second idle disconnect; all cancellable. |
| Concurrency | Maximum 16 connections/device, one in-flight operation/connection, bounded queue; excess fails generically. |
| Reader | Mask after 60 seconds without interaction; best-effort owned clipboard clear after 30 seconds, immediate mask on session lock. Never extends grant. |

Return stable nonsecret codes such as `Unavailable`, `Denied`, `NeedsOnlineRevalidation`, `UnsupportedProtocol`, `InvalidEnvelope`, `TooLarge`, `Busy`. Operational logs may include opaque IDs, versions, outcome and correlation ID under protected audit retention, never secrets, notes, titles, usernames, keys, enrollment challenges or full grants. Offline access audit is best effort queued protected state; it is not proof against a privileged attacker and cannot silently leak metadata to unauthenticated clients.

## 8. Signing trust, certificates and acceptance tests

Signing public keys/trust bundle originate in administrator-controlled install/configuration, not first sync (no TOFU). A transition bundle is signed by an already trusted authorized key and has monotonic bundle version, explicit key IDs/usages and bounded activation/retirement times. Retire old keys without outliving grant caps. A response containing an unknown public key cannot establish trust in itself. Emergency root compromise requires separately authenticated administrator distribution and denial/revalidation, not self-signed response acceptance.

Cert expiry or known revocation denies reads even if snapshot/grant expiry is later. Online issuance requires current revocation evidence. Offline access uses only previously validated evidence up to its signed cap; this can shorten 48 hours. Authentication failures do not masquerade as authorized refresh. Recovery from compromised authentication or unwrap key requires administrator-reviewed device/key generation change and new grants.

Required core fixtures: canonical signature cross-language vectors, wrong context/key, tampered nonce/AAD/ciphertext/tag/wrapped key, mismatched SID/provenance/device/vault/digest, auditor-only and mixed-role capability, explicit assignments, expiry boundary, rollback, reboot, same-boot suspend simulation, deny/allow order permutations, stale response after deny, complete vs partial manifest and crash-recovery state transitions. These tests validate policy only.

The architecture matrix remains the required-evidence contract. The lab is now domain joined, and integrated service/identity/blocked-path read checks have executed in addition to the earlier primitive probe. Use [executed evidence](sprint-1-evidence.md) and [review status](review-status.md) for the case-specific results and remaining gates. Neither a primitive self-spawn test nor an existing interactive domain token establishes offline Windows cached sign-in. **Sprint 1 remains OPEN; no production-readiness claim is made here.**

## Primary Microsoft references

* [CNG key storage properties](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers) — key security/export properties; validate concrete provider behavior on Windows.
* [Named pipe security and rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights) and [CreateNamedPipe](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea) — explicit DACL, create-instance access and remote-rejection requirements.
* [ImpersonateNamedPipeClient](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-impersonatenamedpipeclient) — check failure before treating a caller as authenticated; revert impersonation.
* [Interrupt time](https://learn.microsoft.com/en-us/windows/win32/sysinfo/interrupt-time) — unbiased versus sleep-inclusive clock distinction. Does not establish a Go boot-identity adapter or prove suspend behavior.
