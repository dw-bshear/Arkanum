# Arkanum Go — Sprint 1 architecture

**Status: production architecture proposal; issue #38 under #37 remains OPEN pending the remaining Windows acceptance gates.** A separate synthetic HTTPS server and Windows SCM/CLI security proof now implement a narrower executable contract. They are not the production WPF reader, web sync API, or beta installer. See [review status](review-status.md), [Windows proof](proof-windows.md), and [executed evidence](sprint-1-evidence.md). Passing simulated tests does not prove OS enforcement. Proposed production wire/state requirements are in [protocol.md](protocol.md).

## Verified repository baseline

| Source | Observed baseline and consequence |
| --- | --- |
| `global.json`, `Arkanum.Web/Arkanum.Web.csproj` | SDK `10.0.402` with `latestPatch`; web targets `net10.0`. Use .NET 10, not a separate language/runtime stack inferred from the name “Go”. |
| `Arkanum.Web/Services/DirectoryLdapImportService.cs` | Requests configured attributes plus `entryUUID`/`uid`; `ExternalId` is `entryUUID` or DN. Does not import binary AD `objectSid`/`objectGUID` or bind external IDs to directory provenance. Imports are disabled pending activation. Existing identity data is not sufficient to issue Windows SID grants. |
| `Arkanum.Web/Data/ArkanumDbContext.cs` | `AppUserEntity` has no Windows SID/provenance mapping; `VaultEntity` has no offline switch. Device enrollment, separate device assignment and Go authorization records require server work. |
| `Arkanum.Web/Program.cs` | Cookie and OIDC schemes, not Integrated Windows Authentication. Go cannot infer a Windows user from a web session or claim that current web login proves domain identity. |
| `Arkanum.Web/Services/ArkanumRoleCatalog.cs`, `VaultRepository.cs` | Reveal is a capability, not mere inventory access: `User` required; auditor-only denied; managers/administrators have broader vault authority only after capability check. Existing mixed-role behavior must be preserved deliberately. |
| `docs/1.1.1-release.md` | Web release line is independent of the proposed Go beta. |

No code change or migration is authorized by this document. In particular, the current DN fallback must not be repurposed as a Windows authorization identity.

## Proposed decisions (ADR register)

| ADR | Decision | Tradeoff / acceptance gate |
| --- | --- | --- |
| GO-01 | .NET 10 cross-platform policy/crypto/state core; `net10.0-windows` WPF read-only reader and Windows service. Beta target Windows 11 x64. | Linux core tests are useful but cannot validate CNG, SCM, pipe tokens, session events or clocks. Native evidence required. |
| GO-02 | Machine broker runs as unique virtual account `NT SERVICE\ArkanumGoSync`, not shared LocalService/NetworkService or routine LocalSystem. | Service is trusted to decrypt all assigned device payloads. Compromise of service defeats per-user broker policy. Installer privilege/ACL setup needs review. |
| GO-03 | Shared machine encrypted cache; only the broker unwraps keys. Standard users access through authenticated local-only named pipe. | Enables logged-out refresh and first-ever user read without that user's profile/key being present. This is broker-mediated isolation, not per-user cryptographic inability of the broker to read. |
| GO-04 | Per-vault **Available offline** control with explicit device assignment; existing user-to-vault membership and effective secret-reading entitlement remain the user authorization source. | User membership never enrolls a device; device assignment never grants every local user access. No additional user-offline-assignment administration is required by the agreed foundation. Device groups deferred beyond beta. |
| GO-05 | Signed per-device/per-vault/per-SID grants plus encrypted current-state snapshots. | On-premises server remains trusted to export plaintext into encrypted device envelopes. No cloud service, public identity provider, telemetry or cloud key service is required. |
| GO-06 | Offline access must survive normal laptop reboots within the original authorization window; reboot never renews it. | **Owner requirement confirmed. The same-boot-only proposal is superseded.** The core now implements the reviewed [cross-boot policy](cross-boot-policy.md); real offline reboot/cached-sign-in and powered-off-expiry acceptance remain pending. |
| GO-07 | Go product version `1.0.0-beta.1`; product-qualified tag `arkanum-go-v1.0.0-beta.1`. | Independent of web `1.1.1`. Minimum required server version and compatibility matrix remain undecided until Sprint 2. Do not promise web 1.1.1 compatibility. |

All vault creation, editing, membership/assignment management and secret rotation remain web-only. Reader offers authorized list/read/reveal/copy, not offline writes, conflict merging or a second administration surface.

## Authorization and directory mapping

The server resolves the durable app `UserKey` to a **verified** directory binding `(directoryId, directoryType, authorityId, objectGuid, objectSid, mappingVersion)`. `directoryId` is an administrator-controlled immutable connector identity; `authorityId` records verified AD forest/domain provenance, including domain SID, not merely a hostname entered by a caller. Retrieve AD `objectGUID` and binary `objectSid` over authenticated, certificate-validated directory transport; validate lengths, SID structure, uniqueness and expected authority. Record verification source/status and lifecycle changes. Do not parse binary SID with generic `.ToString()`.

Generic LDAP may have `entryUUID` and no AD security principal. It must remain usable for web identity, but is **not automatically eligible for Go SID grants**. Require an explicitly verified AD mapping before Windows offline use. DN, UPN, username, email and display name are not authorization joins. Reject missing/ambiguous/duplicate bindings; do not accept local-account lookalikes, request-supplied SID, group SID or SIDHistory as the user's current account SID. Deleted/recreated accounts, domain migration/SID changes or provenance changes invalidate the old binding and grants; administrator-reviewed remapping and fresh authorization are required. Windows host SMB/WinRM reachability is not evidence of AD membership, trust, a valid test identity or permission to use credentials.

Effective Go `CanReveal` is the conjunction of:

1. Active app account, directory present and verified binding.
2. Authoritative server secret-reading eligibility for this vault, combining `VaultRepository.CanAccessVaultAsync` and `VaultAuthorizationService.CanRevealAnySecret` with the explicit user-to-vault assignment requirement, plus vault not retired. These are existing building blocks, not a claim that a unified Go authorization method exists.
3. Vault Available offline, explicit active device assignment, current user-to-vault membership, and enrolled/active device. Global inventory/admin visibility alone is not membership.
4. Current valid signed grant matching site/device/vault/app user/provenance/token **user** SID, snapshot, key and policy generations.
5. Valid lease, trusted state, no known applicable deny; authenticated unlocked interactive session.

Auditor-only (`Auditor` with at most `User`) cannot reveal, even with membership and both assignments. Current web `Auditor + VaultManager/Administrator + User` is not auditor-only: use the central effective capability rather than silently changing mixed-role semantics. Administrative inventory access or an enrollment-admin role alone is never an offline reveal grant. Reevaluate role/account/membership/assignment changes on every server refresh; the client consumes signed effective `canReveal`, not a locally interpreted role list.

## Trust boundaries, flows and threats

| Boundary / flow | Required control and residual risk |
| --- | --- |
| Administrator → web control plane → enrollment registry | Authenticated approved operator enrolls a specific device and binds both public keys; one-use proof, no self-approved device. Operator enrollment privilege does not grant vault reads. |
| Directory → server user mapping | Validate transport and provenance; mapping ambiguity fails closed. A compromised authoritative directory/server is outside client secrecy guarantees. |
| Service → on-premises server | TLS plus device certificate/private-key proof, independently of any logged-on user; server authenticates device and evaluates assignments. Prevent MITM, replay and wrong-site substitution. |
| Server → machine cache | Signed grants/deny state and AEAD snapshots; authenticated bindings, version high-water marks and atomic commit prevent ordinary-user tampering, replay and partial-update disclosure. Traffic length/timing and opaque identifiers can still leak structure. |
| Machine cache/CNG → service | Service-only key/cache/state ACLs, nonexportable keys, least privilege. No standard-user cache/key read access. BitLocker is deployment defense in depth, not user authorization. |
| Standard-user WPF → service → WPF | OS-token identity and session gates before any titles, usernames, counts, notes or secrets. Local pipe ACL alone does not establish grant authorization. Untrusted request cannot select an identity, disk path or key. |
| Reader → screen/clipboard | Minimize plaintext lifetime; clear on lock/timeout and best-effort clear owned clipboard only. Authorized copying, screenshots, malware in that user's session and downstream use cannot be revoked. |

The service and Windows kernel/administrators/SYSTEM are trusted boundaries. A hostile administrator, SYSTEM process, debugger, offline image with recovered keys, or VM snapshot rollback is not defeated by file ACLs/software CNG alone. Denial prevents future supported reads, not retrieval from previously copied secrets or privileged snapshots. No absolute secure erase, retroactive secrecy or instantly observable disconnected revocation claims.

### Broker identity and local IPC

Install service binaries/config under administrator-protected paths and cache/state under a service-controlled machine path. Protect each CNG private key with a service-specific security descriptor; creation/use/export permissions must not be granted to ordinary users or unrelated services. Device TLS/signing key and payload-unwrapping key are distinct keys with distinct usage constraints. Do not call a DPAPI `LocalMachine` blob a per-user boundary: machine-scoped DPAPI does not isolate local users. User-profile DPAPI also cannot be a prerequisite for logged-out refresh or first-ever user access.

Proposed pipe `\\.\pipe\ArkanumGoSync.v1`: explicit DACL with narrowly enumerated read/write rights, service-only create-instance rights, deny network/anonymous, and native `PIPE_REJECT_REMOTE_CLIENTS`. Restrict interactive session access and enforce it again in the broker. Never rely on the local-looking pipe name or broad generic write permission. Use first-instance protection and fail rather than operate around a squatter.

Before application data, WPF validates the connected server against SCM's installed `ArkanumGoSync` process and its virtual-account token/service SID; hold process/pipe handles and handle restart/PID reuse races. Process name or pathname alone is insufficient. Failure to inspect/validate means no connection and no sensitive request. Concrete anti-squatting adapter behavior still requires native proof.

Broker authenticates each connection/message via Windows pipe impersonation/token inspection; failure closes the request, never falls back to service identity. Read token user SID, logon/session identity and acceptable authentication level; revert impersonation in guaranteed cleanup before service-owned key work. Bind connection to that identity; request fields cannot override it. Deny remote, anonymous, noninteractive, locked, disconnected or ended sessions. Fast-user switching cannot reuse another session's authorization. Recheck immediately before response, cancel outstanding work and discard plaintext on lock/logoff/disconnect/service restart. Unlock requires a fresh authenticated connection and all lease checks; it does not renew a lease. Reader also masks/clears its view on lock and after its local display timeout. These are required design behaviors, **not implemented OS IPC evidence**.

## Key and cache lifecycle

* An elevated administrator/installer provisions separate nonexportable machine CNG authentication and unwrap keys with service-specific protected ACLs. The virtual service opens existing keys only; a protected one-use receipt binds initial provisioning to the exact configuration and both public keys. Use approved local enrollment to bind device ID, site ID, cert fingerprint and both public-key fingerprints. Possession does not prove hardware attestation or domain membership. See the narrower executable lifecycle in [proof-windows.md](proof-windows.md).
* Server signing keys are separate from TLS/device keys and existing web vault-at-rest keys. Bootstrap trust through administrator-installed configuration/package; never trust a signing key merely because a sync response includes it.
* Fresh random data-encryption key per snapshot generation; wrap only to that device's registered unwrap key. Encrypt all vault names, folder names, titles, usernames, current secret values and note bodies. No history, deleted versions, attachments or recovery/master keys in beta snapshots.
* Rotate authentication cert before expiry with old-key proof and server approval; rotate unwrap key with explicit registry generation and fresh wrapped snapshots. Do not destroy old material until verified replacement commit; expired/revoked key generations cannot authorize fallback reads.
* Server signing-key transition uses a pretrusted signed trust bundle with overlap bounded by existing grant expiry; emergency compromise requires independently authorized trust update and invalidation. Unknown keys fail closed.
* Revocation/offline disable/unenrollment records denial durably before invalidating caches; remove usable references/keys and plaintext best effort. Reenrollment gets a new device generation and no inherited grants. Lost local keys require reenrollment/revalidation, not server master-key delivery. Uninstall/retirement cleanup must not be described as guaranteed physical erasure.

## Lease decision: offline reboot support required

Lease originates at **authorized online validation**, not Windows login, web login, reader start, unlock, cached file access or an unauthenticated successful HTTP response. The proposed maximum is 48 hours, capped by signed expiry/certificate/trust validity. Per-grant server issuance and elapsed time determine the remaining interval; a device refresh cannot silently rejuvenate grants it did not revalidate.

The owner requires normal disconnected reboots and shutdown/startup to preserve access until the original signed expiration. Persist protected lease/deny state and account for powered-off time; reboot, login and service restart must not renew or pause the offline window. A changed boot identity alone is no longer an acceptable reason to require network contact. Missing/corrupt state or detected rollback remains a security recovery condition, distinct from an ordinary restart.

The implemented core uses separate observed-wall and effective-time high-water marks, conservative boot transitions, and a sticky revalidation latch. The Windows proof supplies actual boot identity and sleep-inclusive interrupt time and persists the core state encrypted. Same-boot elapsed-time checks are only one input; powered-off accounting trusts RTC progress. Real sleep/hibernate/Fast Startup, repeated offline reboot, cached sign-in and powered-off-expiry validation are separate gates, not established by the core tests.

**Native acceptance gate:** the former same-boot restriction has been replaced in code, but its historical passing tests must not be relabeled as native cross-boot acceptance. See [cross-boot policy](cross-boot-policy.md) and [GO-06 decision](offline-reboot-decision.md).

## Logged-out refresh and first-ever authorized read

1. Approved device enrollment and separate vault/device + existing vault/user assignments exist; user has verified AD binding and effective reveal capability.
2. With **no interactive user logged on**, SCM starts broker under its virtual account. It proves device certificate/key possession, checks server trust and obtains current per-SID grants and snapshots for assigned vaults. No user cookie/password/profile or user DPAPI key participates.
3. Broker validates signatures, time, generation and deny state and atomically commits encrypted state. Its own key can unwrap payloads; no UI/plaintext cache is created.
4. Disconnect network; the required acceptance flow includes an offline laptop reboot. The authorized domain user signs into Windows using a sign-in method already available offline, then opens Go for the first time on this laptop. First Go use does not mean first-ever Windows domain sign-in: Go does not provision or bypass Windows cached-logon/domain-contact requirements.
5. WPF validates service identity; broker derives token user SID/session, matches signed provenance binding, checks all gates and returns only allowed read data. A different unassigned/new/local/auditor-only user is denied before metadata. No prior Go launch or user key provisioning is required.
6. Lock/logoff closes access. Unlock or normal offline reboot rechecks the remaining original authorization; neither renews it. Online authorized revalidation is the only renewal path. The cross-boot policy is implemented; the full real reboot/cached-domain-sign-in flow remains a native acceptance gate.

## Required evidence / unresolved gates

The approved non-production Windows lab is now domain joined. Directory provenance was collected independently over authenticated, certificate-validated LDAPS. Installed virtual-service/key/cache isolation, zero-interactive-user refresh, an authorized domain-token read with domain/proof paths blocked, and independent unassigned-domain/same-name-local denial have executed. That blocked-path read is **not** an offline Windows cached sign-in or offline reboot; its saved report alone also does not independently prove the reader had never launched Go previously. Keep core policy, native primitive and integrated domain evidence separate; use [executed evidence](sprint-1-evidence.md) for actual results rather than interpreting this required-evidence matrix as a list of passes.

| Validation | Required native evidence |
| --- | --- |
| Approved test environment | Approved non-production Windows 11 x64 laptop joined to the lab domain, independently verified directory provenance, standard domain users, and local administrator recovery. Operational identities and raw evidence remain private. Offline console sign-in requires the owner's physical participation; do not replace it with an online RDP logon. |
| Service identity / shared cache | Real SCM virtual-account token; CNG key use succeeds logged out; unrelated service and standard users cannot read keys/cache or unwrap snapshots. |
| Logged-out → first-ever reader | Refresh with zero interactive sessions, encrypted disk inspection, first Go use by assigned AD token, matching plaintext only after authorization. |
| IPC identity / anti-squatting | Spoofed request SID, wrong token, remote SMB pipe client, anonymous client, forged pipe server, service/PID restart race and create-instance attempts all denied. |
| Sessions | Lock/logoff/disconnect/fast switching during pending reveal; no late plaintext; unlock without lease renewal. |
| Clock adapter | Actual sleep, resume, hibernate, Fast Startup, reboot, service restart, rollback, forward jump, corrupt/missing anchors and expiry boundary. Verify boot identity and sleep-inclusive timer, not mocks. |
| Cache / revocation | Crash at each commit stage; concurrent deny vs stale allow, cert expiry/revocation, signature rotation, altered AEAD and replayed generation. |
| Product/security review | Implement and review the confirmed cross-boot requirement, not the superseded same-boot restriction; review cryptographic/wire limits, AD mapping migration and Sprint 2 server compatibility. |

Sprint 1 stays OPEN until required Windows results are recorded and reviewed. Linux core tests and this design are not substitutes for native IPC/shared-cache/time evidence or production readiness.

## Primary Microsoft references

* [Service and virtual accounts](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/understand-service-accounts) — virtual-account naming and network context; Go still requires independent certificate authentication.
* [AD security identifiers](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/understand-security-identifiers) and [objectGUID](https://learn.microsoft.com/en-us/windows/win32/adschema/a-objectguid) — distinguish object identity, current SID and mutable names.
* [CNG key storage properties](https://learn.microsoft.com/en-us/windows/win32/seccng/key-storage-property-identifiers) — export policy and key security descriptor.
* [DPAPI CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata) — machine scope is not individual-user isolation.
* [Named pipe security](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights), [CreateNamedPipe](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea), [pipe server PID](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid), [pipe impersonation](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-impersonatenamedpipeclient) — primitives, not proof of Go's adapter.
* [Interrupt time](https://learn.microsoft.com/en-us/windows/win32/sysinfo/interrupt-time) and [QueryInterruptTime](https://learn.microsoft.com/en-us/windows/win32/api/realtimeapiset/nf-realtimeapiset-queryinterrupttime) — clock selection; native continuity/suspend testing still required.
