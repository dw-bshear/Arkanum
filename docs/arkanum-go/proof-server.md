# Sprint 1 HTTPS proof server

This is an isolated proof host, **not a production offline-vault service**. It never opens a vault database, uses a production data encryption/recovery key, or exports production secrets. Every encrypted package contains the UTF-8 JSON in `ProofService.SyntheticPayload`: explicitly marked made-up **test data**, with both a credential (`credentials[0].username` / `password`) and a secure note (`secureNotes[0].text`). No actual portal credential/note is read. Verification compares the SHA-256 content hash, never logs decrypted plaintext, and must cover both JSON collections.

## Projects and commands

- `Arkanum.Go.Proof.Protocol/`: shared wire records, domain-separated canonical response encoding, pinned RSA-PSS response verification.
- `Arkanum.Go.Proof.Server/`: runnable ASP.NET Core/Kestrel HTTPS host; references the actual `Arkanum.Web` project for `VaultAuthorizationService` and role semantics.
- `Arkanum.Go.Proof.Tests/`: cryptographic negatives, persisted sequence/deny tests, real loopback mTLS, executable-server launch, and a separately launched **actual Arkanum.Web executable** with a disposable test database/key.

From the checkout, with .NET 10 on PATH:

```sh
dotnet build Arkanum.Go.Proof.Server/Arkanum.Go.Proof.Server.csproj -c Release
dotnet test Arkanum.Go.Proof.Tests/Arkanum.Go.Proof.Tests.csproj -c Release
```

If the SDK executable is not on PATH, set `PROOF_DOTNET` to its absolute path when running tests. Tests use `DOTNET_HOST_PATH` as the next fallback. Integration tests temporarily trust only per-run, disposable self-signed certificates in the current user's root store and remove those exact certificates in `finally`/`Dispose`. They do not install a validation callback or accept a hostname mismatch. Run tests under a disposable unprivileged test account if changing even temporary user-level trust is undesirable. Tests use `Path.GetTempPath()` (and thus the runner's `TMPDIR`) and clean their private working directories. An abruptly killed test process may require removal of its disposable root certificates.

## Operator inputs — never commit operational configuration

Keep all actual registry/configuration, certificates, passwords, signer keys, identity evidence and sequence state outside source control. For a local isolated run, the existing ignored `Arkanum.Go.Proof.Server/artifacts/runtime/` is suitable; production-like proof runs should use an administrator-owned external directory. Restrict directory/key/password/state ACLs to the service and the enrolling administrator. Do not store any recovery key here.

Supply four **absolute file paths** as environment variables:

| Variable | File contents |
|---|---|
| `PROOF_REGISTRY_FILE` | Operator-created JSON registry, schema below |
| `PROOF_TLS_PFX_FILE` | HTTPS certificate and private key in PKCS#12; correct DNS SAN, server-auth EKU and trusted chain |
| `PROOF_TLS_PASSWORD_FILE` | PFX password; trailing CR/LF stripped, other characters preserved |
| `PROOF_SIGNING_KEY_FILE` | RSA PKCS#8/PKCS#1 private-key PEM, at least 2048 bits; pin its public SPKI in the broker independently of HTTPS |

Do not reuse the TLS, response-signing, device-authentication or device-unwrap key for another purpose. Device private keys stay on the device; the server receives **only** the approved client certificate's SHA-256 fingerprint and the separate RSA unwrap public SPKI. Authentication and unwrap keys must be distinct. Certificate fingerprints and unwrap SPKIs must each be unique across registered devices. Client certificate authentication requires RSA >=2048 bits; the TLS stack verifies the certificate chain, time and client-auth usage, and the server verifies the exact approved fingerprint. Kestrel requires a client certificate and enables certificate revocation checking. Provision reachable CRL/OCSP infrastructure for CA-issued client certificates; a chain/revocation failure must not be bypassed for the proof. Install the legitimate issuer chain in the service account's trust store and the HTTPS issuer chain on the device. Broker HTTPS must use ordinary chain and hostname validation, without a permissive certificate callback.

Launch after provisioning the four files and registry:

```sh
export PROOF_REGISTRY_FILE=/absolute/private/proof/registry.json
export PROOF_TLS_PFX_FILE=/absolute/private/proof/https.pfx
export PROOF_TLS_PASSWORD_FILE=/absolute/private/proof/https-password.txt
export PROOF_SIGNING_KEY_FILE=/absolute/private/proof/response-signer.pem
dotnet run --no-launch-profile --project Arkanum.Go.Proof.Server/Arkanum.Go.Proof.Server.csproj -c Release
```

These are path placeholders, not existing endpoints or credentials. The registry is loaded once at startup. Stop/restart the isolated proof host after changing approval, user activation, membership, assignment or offline switches. No existing deployment is reconfigured by this project.

## Registry schema

JSON property names are case-insensitive; enum role names are the actual `Arkanum.Web.Domain.ArkanumRole` names. Machine-readable operator reference: `Arkanum.Go.Proof.Server/proof-registry.schema.json` (JSON Schema draft 2020-12; canonical property casing). Schema validation is not directory verification and does not replace the loader's uniqueness, SID, provenance and key checks. The following is a **non-runnable schema illustration**, not directory evidence. Replace every placeholder from independently verified enrollment evidence in an ignored/private registry file.

```json
{
  "ListenAddress": "127.0.0.1",
  "Port": 7443,
  "LeaseSeconds": 172800,
  "StateDirectory": "<absolute private writable state directory>",
  "EnrollmentDirectory": "<absolute separately protected enrollment-marker directory>",
  "Users": [
    {
      "Id": "<operator stable user reference>",
      "Sid": "<verified AD objectSid>",
      "DomainSid": "<verified AD domain SID>",
      "ObjectGuid": "<verified AD objectGUID>",
      "Domain": "<verified directory DNS domain>",
      "VerifiedBy": "<operator / private evidence reference>",
      "VerifiedAt": "<UTC directory verification timestamp>",
      "Enabled": true,
      "Roles": ["User"]
    }
  ],
  "Devices": [
    {
      "Id": "<unique enrollment reference>",
      "Approved": true,
      "CertificateSha256": "<64 uppercase hexadecimal SHA256 certificate fingerprint>",
      "UnwrapSpki": "<canonical Base64 DER RSA SubjectPublicKeyInfo, separate CNG key>",
      "IssuedAt": "<fixed UTC enrollment issuance timestamp>",
      "VaultIds": ["<explicitly device-assigned synthetic vault reference>"]
    }
  ],
  "Vaults": [
    {
      "Id": "<synthetic vault reference>",
      "OfflineEnabled": true,
      "MemberUserIds": ["<explicit user membership reference>"]
    }
  ]
}
```

`VerifiedBy`/`VerifiedAt` are trusted operator provenance, **not server-performed LDAP verification**. This host does not query LDAP or turn arbitrary SID-looking input into identity evidence. Before a real-directory proof, the operator must verify `objectSid`, `objectGUID`, the domain SID/name and account state using authenticated, normally validated LDAPS, record the evidence privately, and explicitly approve application access/roles. Include an independently verified unassigned AD account for the negative proof, but do not add it to any assigned vault's `MemberUserIds`. Merely being a known registry user never grants access. Test source uses conspicuously synthetic AD-shaped identifiers solely for automated tests; it establishes no real AD identity claim.

User IDs, SIDs and GUIDs must be unique and nonempty; the SID must belong to the supplied domain SID. Empty/missing GUID/provenance, future verification, disabled user, auditor-only role or missing actual reveal capability cannot authorize a package. Administrative/vault-manager privileges **do not substitute for** the actual Web `CanRevealAnySecret` policy, user-vault membership, or separate device-vault assignment. There is no group membership inference. For **each device-assigned vault**, derive grants from that vault's existing `MemberUserIds`, restricted by enabled/provenance/identity uniqueness and effective reveal policy. Users need not belong to other assigned vaults. Legacy `Devices[].UserIds` is optional compatibility metadata only (including omitted, empty or null): it neither grants access nor restricts eligible members. Do not maintain an extra per-device user allowlist.

Unknown/invalid/ineligible members, invalid/offline-disabled assigned vaults, no assigned vaults, and empty effective authorization conservatively deny the **entire device response**. Persisted state tracks every previously granted vault/SID/objectGUID binding. Removing a formerly granted user, membership or vault assignment, or disabling/restricting a member, triggers complete signed-device-deny instead of a partial update that could strand a cached SID. Unrelated unassigned users receive no packages and do not block grants. This conservative proof does not selectively evict individual SIDs; a deny clears every cached package and stays latched, requiring explicit re-enrollment.

## API and signed protocol

Only `POST /sprint1/sync` exists on this proof host. JSON request:

```json
{"DeviceId":"<enrollment reference>","Nonce":"<Base64 32 fresh cryptographically random bytes>"}
```

The body is limited to 4096 bytes. Missing/untrusted client certificates fail TLS. A trusted but unregistered/mismatched certificate, duplicate certificate pin or shared/auth-equal unwrap key cannot authenticate the device (HTTP 403). Malformed requests return HTTP 400. A recognized device lacking approval/authorization receives HTTP 200 with a **complete signed denial**: `Denied=true`, zero `Packages`, echoed device and nonce, monotonic sequence, UTC server time and RSA-PSS/SHA-256 signature. Do not confuse transport/JSON success with permission.

The exact shared records live in `Arkanum.Go.Proof.Protocol`:

```csharp
record ProofSyncRequest(string DeviceId, string Nonce);
record ProofPackage(SignedGrant Grant, EncryptedSnapshot Snapshot, byte[] WrappedKey);
record ProofSyncResponse(string Protocol, string DeviceId, string Nonce,
    long Sequence, DateTimeOffset ServerUtc, ProofPackage[] Packages,
    bool Denied, byte[] Signature);
```

`ProofWire.ProtocolId` is `arkanum-go-sprint1/1`. Sign/verify **`ProofWire.EncodeUnsigned(response)`**, not JSON. Its fixed-order encoding starts with a separate response domain string, then protocol/device/nonce/sequence/server UTC ticks/deny/package count and each complete package (canonical grant, grant signature, GCM nonce/ciphertext/tag, wrapped key). It excludes only the outer signature. Strings use strict UTF-8 with BinaryWriter's 7-bit byte-length prefix; numbers are little-endian; byte arrays have signed Int32 byte lengths; UTC timestamps are .NET ticks; package order is significant.

Each grant is separately RSA-PSS/SHA-256 signed over `GrantCanonical.Encode`. A random 32-byte AES key encrypts the synthetic payload with AES-256-GCM (12-byte nonce, 16-byte tag), with the **exact canonical grant bytes as AAD**. RSA-OAEP/SHA-256 wraps that key to the device's separately pinned unwrap key. No plaintext AES key leaves the server, and temporary key bytes are zeroed.

## Authenticated refresh, immutable grants, replay resistance and deny handling

`Devices[].IssuedAt` is fixed **enrollment provenance**, durably pinned at first sync. It must not be changed to renew a lease; a registry/state timestamp mismatch (or future enrollment) produces a signed deny. An enrollment older than 48 hours remains eligible for an authenticated authorized refresh.

Each successful mTLS-authenticated, authorized sync issues **new signed grants** at the current server time: grant `IssuedAt == response.ServerUtc`, `ExpiresAt = min(ServerUtc + LeaseSeconds, authenticating client certificate.NotAfter)`. This applies even when both synthetic credential/note content and `ContentVersion` are unchanged. `LeaseSeconds` is an integer in **1..172800 inclusive**; omission defaults to **172800 (48 hours)**. Invalid values fail configuration loading and are also rejected by the service itself. The certificate cap can make the effective lease shorter than the configured duration.

An existing signed grant is immutable: local reads, clock rollback, reboot, disconnected operation and server restart do **not** change its issuance or expiry. Only a fresh successful signed response with the fresh request nonce and a higher durable sequence can install a renewed window. Failed TLS/authentication/schema requests issue no grants and do not advance durable sequence. A signed deny advances the sequence, carries no grants and never renews a lease.

`EnrollmentDirectory` is a **required, absolute, independently protected directory**, separate from and not nested with `StateDirectory` (including the reverse nesting). Provision physically distinct non-symlink paths with administrator-controlled ACLs: ordinary sequence-state cleanup/restore operators must not be able to remove enrollment markers. Each device gets a write-once `.enrolled` marker, exclusively created and flushed before first issuance. If its established sequence/deny state disappears while the marker remains, sync fails closed with no grants and no sequence-zero recreation; the host returns an operational error, not a guessed lower-sequence signed deny. Unrelated pristine enrollments continue normally. If sequence state exists but its marker is missing, only a conservative signed deny can be issued, with its known next sequence.

State created by the older proof without complete authorization-history bindings is **conservatively denied on first refresh**, not silently upgraded to a partial response. Re-enroll explicitly; never delete sequence state or enrollment markers to bypass a deny. Neither directory can protect against a privileged actor deleting/rolling back **both** independent stores; that remains an explicit privileged rollback limitation.

The server serializes per-device state updates using exclusive file locks and writes/flushed replacement state before returning the signed response. Sequences persist across process restart. Once a complete deny is issued, the server durably latches deny for that device; merely restoring membership/approval does not clear it.

The broker must generate a fresh nonce for **each** request, verify the signer pin and canonical signature, require exact device/nonce/protocol match and a strictly greater durable sequence, and persist the sequence/deny latch **before** making any package visible. `ProofResponseVerifier.Accept` provides cryptographic/envelope checks with caller-supplied last sequence/latch; it does **not** persist broker state or authorize decrypted grants. A valid denial must clear cached packages/keys and latch the Windows service deny. A deny-latched broker must not accept an older successful response, even if its grant has not yet expired. Core `SnapshotProofReader` must still verify grant signature, context, that specific grant's immutable expiry, content hash and authenticated encryption with the trusted lease clock.

This minimal proof requires explicit administrator re-enrollment after deny. Prefer a **new device enrollment ID and fresh authentication/unwrap keys**, coordinated broker cache/latch removal and new fixed enrollment issuance time (separate from each grant's fresh issuance). Do not just delete sequence state, roll it back, or edit `IssuedAt`. There is no production supersession/recovery protocol. State files and signer keys require protected, durable storage; this file-based proof does not resist privileged administrator rollback, restore-from-old-backup, or filesystem/power-loss attacks as a production monotonic state service would.

## Publish and native-device short-expiry proof

Publish the **proof host**, never the portal executable, to a fresh versioned directory outside the checkout on the deployment machine:

```sh
dotnet publish Arkanum.Go.Proof.Server/Arkanum.Go.Proof.Server.csproj \
  -c Release --self-contained false -o /absolute/private/proof/publish-current
# Set the four PROOF_* absolute file paths above in the service environment.
dotnet /absolute/private/proof/publish-current/Arkanum.Go.Proof.Server.dll
```

The publish is framework-dependent and requires the .NET 10 ASP.NET Core runtime on the host. Publish output includes referenced Web policy assemblies; launch **only `Arkanum.Go.Proof.Server.dll`**, not `Arkanum.Web.dll`. Do not copy a real portal database, site recovery/master key, private runtime registry or certificates into a distributable publish archive. Never publish over a running deployment: stop the proof host, deploy a fresh directory, preserve its external durable `StateDirectory` **and independently protected `EnrollmentDirectory`**, set the four file paths, then start the new proof executable. The registry and lease configuration are startup snapshots; restart after changes. A non-loopback deployment requires an operator-approved private `ListenAddress`, network/firewall route and correct HTTPS DNS SAN/trust. No `--urls`, TLS bypass, certificate-to-user mapping or portal login should be introduced.

For a real native expiry run, privately set **`"LeaseSeconds": 90`**, restart this proof host, and perform one successful broker refresh using the actual approved device certificate and its separate unwrap key. Require verified signature/nonce/sequence, grant issuance equal to signed server UTC and an expiry 90 seconds later (or earlier if the client authentication certificate expires first). Hash-check decrypted UTF-8 JSON and verify **both** the synthetic credential and secure-note fields, without logging their plaintext. Disable subsequent refresh/network access and observe access stop after the signed deadline using ordinary elapsed time: no machine-clock changes, local-read renewal or reboot renewal. For the unchanged-content renewal proof, obtain a second successful refresh **before** disconnecting and show its newer signed issuance/expiry with the same content hash; retain the earlier signed grant to show its original expiry never moved. Failed requests/denial must not create a renewed window.

The automated full HTTPS suite includes an actual 90-second signed lease and waits its real elapsed deadline before checking local expiry; allow about 95 seconds for the suite. It also tests default/min/max lease configuration, certificate-expiry caps, ten-day-old enrollment refresh, authorization removal and deny latching. Windows CNG/broker deployment remains a separate native acceptance step.

## Separation from portal authentication and proof limits

The proof host does not register a certificate-to-user authentication handler, set a user cookie, or map portal/admin/edit routes. Referencing `Arkanum.Web` imports policy code, **not** its hosted routes or database. Tests additionally launch the real unchanged Web entry point with mTLS and confirm a device certificate alone cannot GET protected Users/Settings/Vaults/details/operational endpoints or POST mutation handlers. This is stronger than checking fake route stubs; no operational deployment is touched.

Automated tests prove local software/cryptographic/HTTPS behavior with disposable synthetic fixtures, not real AD enrollment, Windows CNG key non-exportability, Windows broker persistence, deployment PKI/revocation reachability or production readiness. Those require the separate real-directory and Windows-device proof. The operator must populate verified directory records and bind the actual approved device pins before claiming that proof.
