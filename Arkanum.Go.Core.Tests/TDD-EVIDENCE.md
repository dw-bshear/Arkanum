# Executed TDD evidence — isolated core

Host: Linux; SDK `$HOME/.hermes/cache/scratch/arkanum-go-dotnet/dotnet`, observed version `10.0.402`. These are executed results, not proposed tests. Existing solution and web projects were not changed or tested by this isolated suite.

Every command below uses:

```sh
export DOTNET_ROOT=$HOME/.hermes/cache/scratch/arkanum-go-dotnet
D="$DOTNET_ROOT/dotnet"
P=Arkanum.Go.Core.Tests/Arkanum.Go.Core.Tests.csproj
```

Tests were authored before the relevant implementations. Initial compilable method skeletons deliberately threw `NotImplementedException`; RED runs therefore exercised test discovery and assertions rather than relying on missing-type compilation failures. Implementations replaced those skeletons only after the observed RED results.

| Cycle | Executed command (logger omitted) | Observed result |
|---|---|---|
| SID RED | `"$D" test "$P" --filter FullyQualifiedName~AdSidTests` | 30 failed, 0 passed: deliberate NotImplementedException instead of parsed SID / FormatException. |
| SID GREEN | `"$D" test "$P" --no-restore --filter FullyQualifiedName~AdSidTests` | 30 passed, 0 failed. |
| Clock RED | `"$D" test "$P" --no-restore --filter FullyQualifiedName~LeaseClockTests` | 7 failed, 0 passed: deliberate NotImplementedException. |
| Clock GREEN | `"$D" test "$P" --no-restore` | 37 passed, 0 failed. |
| Canonical encoding RED | `"$D" test "$P" --no-restore --filter FullyQualifiedName~SnapshotProofTests` | 54 failed, 0 passed: deliberate encoder NotImplementedException. |
| Encoder GREEN / reader RED | same filtered command after encoder implementation | 1 passed (canonical UTC normalization), 53 failed on the deliberately unimplemented reader. |
| Crypto GREEN | `"$D" test "$P" --no-restore` | 91 passed, 0 failed. |
| Additional fail-closed RED | `"$D" test "$P" --no-restore --filter 'FullyQualifiedName~Invalid_persisted_state\|FullyQualifiedName~Invalid_context_floors\|FullyQualifiedName~Missing_snapshot_components'` | 7 failed: invalid state was repaired, negative trusted floors admitted plaintext, absent snapshot components threw NullReferenceException. |
| Fail-closed GREEN | `"$D" test "$P" --no-restore` | 98 passed, 0 failed. |
| Exact version RED | `"$D" test "$P" --no-restore --filter FullyQualifiedName~VersionTests` | 1 failed: SDK automatically appended source revision to InformationalVersion. |
| Exact version fix | Set `IncludeSourceRevisionInInformationalVersion=false` in both new projects only. | Verified by final tests, including assembly/file/informational version reflection checks. |

The additional fail-closed filter above displays pipes escaped for Markdown; pass literal `|` characters inside the shell single quotes.

## Final verification

```sh
"$D" restore "$P" --locked-mode
"$D" test "$P" --no-restore --logger 'console;verbosity=minimal'
"$D" test "$P" -c Release --no-restore -warnaserror \
  --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=green-release.trx'
```

- Locked restore: succeeded for both projects.
- Debug: **99 passed, 0 failed, 0 skipped**.
- Release with warnings as errors: **99 passed, 0 failed, 0 skipped**.
- Local machine-readable final result: `TestResults/green-release.trx` (ignored build evidence).
- Local full RED logs: `red-sid.log`, `red-clock.log`, `red-canonical.log`, `red-reader.log`, `red-hardening.log`, `red-version.log` (ignored).

Real RSA-PSS signatures and real AES-256-GCM operations are used. No stub cryptography, mocked signature boolean, server or network integration is used. All original NotImplementedException skeletons were replaced. The full native Windows gate remains **OPEN**: these tests do not exercise authenticated pipe impersonation, pipe/file ACLs, Windows key protection, durable anti-rollback storage, service lifecycle or hourly unattended sync.

## Parent review hardening

The parent independently reran locked restore and all 99 Release tests, then added four parameterized cases requiring a clock failure to remain denied even after later readings look valid.

- RED command: `dotnet test Arkanum.Go.Core.Tests/Arkanum.Go.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Clock_failure_stays_denied`.
- Observed RED: **3 failed, 1 passed**. Wall rollback, monotonic rollback and a changed boot could recover offline when later values matched the old state. Overflow was already denied by its advanced high-water.
- Fix: add a persisted `NeedsOnlineRevalidation` latch; clock/state failure sets it, and offline evaluation cannot clear it.
- GREEN: **103 passed, 0 failed, 0 skipped**, Release with warnings treated as errors.
- Parent machine-readable result: `artifacts/arkanum-go-sprint1/core-tests/parent-go-core.trx` (ignored build evidence).

The latch itself is only as durable and trustworthy as the future broker persistence adapter. These tests do not make it rollback-proof against administrator/SYSTEM or restored snapshots.

## Independent security-review correction

The independent reviewer found that the clock compared observed wall UTC against an effective time that could already be ahead due to monotonic elapsed time. A second frozen/nondecreasing wall reading therefore incorrectly latched a rollback failure.

- Added two chained clock tests (frozen wall and slowly advancing wall) and a signed-snapshot test spanning repeated frozen-wall reads through the exact expiry boundary.
- RED filter: `FullyQualifiedName~Nondecreasing_wall|FullyQualifiedName~Repeated_frozen_wall`; **3 failed, 0 passed** as expected.
- Fix: `HighWaterUtc` now records only observed wall-clock high-water. Effective expiry time separately takes the maximum of that observed value and the monotonic-derived estimate. The persisted elapsed high-water preserves monotonic progress; true regression still latches online revalidation.
- Updated two old tests that incorrectly expected observed wall high-water to contain extrapolated effective time; their effective-time and monotonic progress assertions remain explicit.
- Final parent Release run with warnings treated as errors: **106 passed, 0 failed, 0 skipped**.
- Machine-readable reviewed result: `artifacts/arkanum-go-sprint1/core-tests/parent-go-core-reviewed.trx`.
