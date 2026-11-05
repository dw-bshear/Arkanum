# Sprint 1 executed evidence and remaining gates

**Outcome: integrated security proof implemented, with remaining native acceptance gates; issue #38 stays OPEN.** This is not a WPF beta application, production web sync API, production installer or release. The existing web application and version are unchanged.

The reviewed core/protocol/synthetic-server foundation was published at `136e3de0eddcd12e4aeda8e8a8b84a353409801b` on `feature/arkanum-go-sprint-1`. Final Windows source review and native rerun results are recorded in [review-status.md](review-status.md); use Git history and issue #38 for the subsequent publication commit. See [sanitized results](sprint-1-evidence.json), [native acceptance](native-acceptance.json), [architecture](architecture.md), [production proposal versus executable proof](protocol.md), and [remaining manual validation](validation.md).

## Executable artifacts

- `Arkanum.Go.Core` / `Arkanum.Go.Core.Tests`: canonical AD user/domain SID and binary objectSid validation, pinned RSA-PSS grant signatures, AES-256-GCM snapshot authentication, conservative cross-boot lease policy and sticky revalidation state.
- `Arkanum.Go.Proof.Protocol` / `Arkanum.Go.Proof.Server` / `Arkanum.Go.Proof.Tests`: separate synthetic-data HTTPS/mTLS host; independently protected enrollment and sequence state; effective Web reveal policy plus explicit vault membership and separate device assignment; real-time lease renewal/expiry and signed sticky deny.
- `Arkanum.Go.Proof.Windows`: real virtual-account Windows SCM service, elevated one-time CNG provisioning, distinct nonexportable machine keys, protected encrypted cache, native clock adapter and authenticated session-aware local pipe reader. The CLI reports only allowed/mode/byte-count/hash, not plaintext.
- `Arkanum.Go.NativeProbe`: older standalone Windows primitive executable using synthetic data and ephemeral keys. Its self-spawn tests are not shared-service acceptance.

## Fresh parent verification — November 5, 2026

| Execution | Observed result | Boundary |
| --- | --- | --- |
| Go core Release | **131 passed, 0 failed, 0 skipped** | Policy/crypto, including controlled cross-boot inputs; not actual reboot acceptance |
| Synthetic HTTPS proof Release | **51 passed, 0 failed, 0 skipped** | Real HTTPS/mTLS, signatures, actual 90-second expiry, unchanged-content renewal, membership/role/device separation and unchanged real portal protection |
| Existing web Release | **266 passed, 0 failed, 0 skipped** | Existing application regression; no production instance changed |
| Windows source-contract/Python checks after ACK correction | **15 passed** | Includes portable crypto and contract executables; not native acceptance by itself |
| Installed broker reader regression after ACK correction | **60/60 passed**: 30 read and 30 status | Actual non-elevated standard domain token, active/unlocked session, alternating requests against one reused installed-service pipe |
| Native lock denial | Exit 10, no allowed response while locked | Actual standard domain-user session; subsequent unlock/read returned exit 0 |
| Native disconnect denial | Exit 10, no allowed response while disconnected | Actual standard domain-user RDP session; not an offline console sign-in |
| Native offline service restart after ACK correction | Sequence and signed expiry unchanged; authorized read exit 0 | Domain/proof paths blocked, remote management retained; this is not a physical network disconnect or OS reboot |

Integrated native history additionally establishes: virtual-service runtime identity; protected nonexportable auth/unwrap machine keys; device-authenticated refresh with zero interactive sessions; an authorized domain-token read with domain/proof paths blocked; independent unassigned-domain and same-name-local read/status/cache/key denial; forged SID rejection; remote SMB pipe rejection; and an impostor broker receiving zero request bytes. Exact historical scope and private evidence handles are recorded in `native-acceptance.json` and the ignored evidence directory. The earlier read was labeled first Go use but occurred in an existing domain session: its saved report alone does **not** independently establish first-ever Go launch or Windows cached domain sign-in. Remote-pipe rejection and directory provenance were observed separately; they are not evidenced by the reader-report JSON files alone.

## Defects found through execution

1. **Rollback could heal offline:** parent regression tests exposed a missing durable revalidation latch. Offline evaluation can no longer clear a detected clock/state fault.
2. **False rollback from extrapolated time:** observed wall UTC and monotonic-derived effective UTC now have separate high-water marks; frozen/slow-wall regression tests cover their distinction.
3. **Primitive collision fixture exercised the wrong limit:** max-instances 1 returned pipe-busy before first-instance protection. The corrected fixture allows two instances by count and requires access-denied, not busy, as the security result.
4. **Virtual-service machine-key creation failed:** native `NTE_PERM` was reproduced. One-time provisioning now runs in the elevated installer; the service only opens validated existing keys. No SYSTEM runtime substitution, administrator membership or broad keystore ACL was used.
5. **Authorized response was discarded during disconnect:** a valid, unlocked native domain reader returned exit 10 despite a matching decryptable package. Completed pipe writes/`FlushAsync` were not proof of reader consumption. The server now retains the same pipe until a bounded acknowledgement sent after the reader's final SCM/PID/service-token validation. A new contract test first failed, then passed; the actual native reader changed from exit 10 to exit 0, followed by 60 successful repeat trials. Temporary identity-hash/package-count diagnostic writes were removed. No authentication, session, expiry or key-isolation check was relaxed.

## Independent review scope

The frozen core/protocol/server review verified source hashes, reran the 131 core and 51 server tests, and exercised six additional scratch-only edge cases without a blocker in that scope. The earlier Windows-source review passed its frozen scope but did not detect the runtime response-delivery race; source review never overrides a failed native test. Any later Windows changes require review and fresh native checks before publication/completion.

## Evidence handling

Raw identity-bearing native reports, operational endpoints, directory provenance, certificates, protected test configuration and TRX logs remain in ignored `artifacts/arkanum-go-sprint1/`. Public evidence contains sanitized outcomes, counts, privilege context, synthetic byte/hash comparisons, source/build hashes and explicit limitations only. No production vault, site recovery/master key or reusable domain password is delivered to the proof broker. Test-account sign-in credentials are lab administration inputs, not Go sync credentials.

## Remaining acceptance gates

- Physical offline Windows cached domain sign-in after ordinary/repeated reboot, authorized read before the original unchanged expiry, and shutdown/startup across expiry with denial. The owner's console participation is needed; online RDP logon is not a substitute.
- Native sleep/hibernate/Fast Startup and relevant rollback/state-corruption/replay/commit-interruption cases; do not relabel pure policy tests or a service restart as an OS lifecycle pass.
- Native auditor-only and signed-deny persistence, remaining effective token/key/file isolation cases, and certificate/revocation/key-rotation qualification within the agreed proof scope.
- Final Windows-source review, sanitized evidence publication and acceptance review.

RTC progress is trusted during powered-off time. Trusted administrator/SYSTEM/service/server compromise and coordinated full-state/RTC snapshot rollback are outside the software-only boundary. Flushed files and atomic replacement do not establish power-loss-safe directory metadata, hardware anti-rollback or secure erasure. No acceptance checkbox is completed merely because a build, primitive test or review passed.
