# Semgrep alert review — October 29, 2026

## Scope and disposition

Reviewed all **155** initial Semgrep OSS alerts from workflow run **40803030214**
against source commit `a74d69e3f8adf45dd8fa03ad5c87686bd4c3014f` in
`dw-bshear/Arkanum`. Source locations below and in the JSON ledger refer to that
commit, not to an unspecified latest release. Both readable and shipped minified
vendor assets were inspected; minified columns were mapped independently.

| Component | Reviewed | False-positive dispositions | Keep open |
|---|---:|---:|---:|
| Bootstrap | 33 | 33 | 0 |
| jQuery Validation / unobtrusive | 63 | 61 | 2 |
| jQuery core | 58 | 46 | 12 |
| Fixed-input C# test | 1 | 1 | 0 |
| **Total** | **155** | **141** | **14** |

`docs/semgrep-triage-2026-10-29.json` contains one entry per alert: exact GitHub
number, rule, location including minified columns, individual rationale, source
references, disposition and the exact proposed GitHub dismissal payload. Entries
are ordered by alert number. `docs/semgrep-triage-2026-10-29-evidence.json`
retains real synthetic-probe observations, including deliberately unsafe controls
and timeouts. These files are evidence of review, not a substitute for checking
current GitHub alert state.

False-positive dispositions are specific to the current application's input
sources and callers. They do **not** assert that each library API is safe for
arbitrary future uses. No alert is dismissed merely because it is vendor code,
minified, or inconvenient. No accepted-risk / "won't fix" disposition is used.

## Findings retained open

### jQuery selector grammar — alerts 105–108 and 140–143

`jquery.js:673–715` builds identifier, attribute and pseudo-selector regexes from
fixed grammar fragments. Those fragments are not necessarily efficient merely
because they are constants. Small malformed identifier and attribute cases show
pathological backtracking; 65- and 66-character cases respectively exceeded a
**2-second subprocess deadline**. The parent repeated those deadline tests.
Corresponding constructed patterns are present in both bundled assets.

The tested pseudo cases matched a prefix quickly; that does not prove the full
pseudo grammar safe. All eight alerts stay open pending stronger reachability,
selector-bound analysis or a verified dependency remediation. No user-field to
pathological-selector flow has been demonstrated in Arkanum; this is not a claim
of a remotely exploitable application denial of service.

### jQuery Validation URL regex — alerts 88 and 102

The actual bundled URL-validation method has overlapping user-info alternatives.
With a synthetic failing input, 24 repeated encoded-character sequences consumed
about **0.88 seconds** in the initial probes, and 28 repetitions exceeded a
**4-second subprocess deadline** in both readable and minified bundles. The
parent independently repeated the two timeouts.

No current application URL-validation rule, `type=url`, URL annotation or custom
rule binding was found. Settings fields that contain URL-looking values are
ordinary inputs. The pathological library behavior is real, but an Arkanum
attacker-to-victim path is unestablished. Keep both alerts open rather than
accepting the risk or assuming a newer upstream version repairs it.

### jQuery generic HTML sinks — alerts 124, 125, 127 and 128

`jquery.js:4770` and `:6171` are unsanitized HTML deserialization/assignment
primitives. `htmlPrefilter` at `:5984–5985` is not a sanitizer. Actual loaded
validation code uses HTML message APIs, so these lower-level sinks are not
universally unreachable. Synthetic controls retained event attributes and, for
the jQuery probe, explicitly dispatched an event that executed a harmless
synthetic marker.

The specific validation-call review found authored/generated fixed-field
messages, not an entered-value or remote-response path in the current app. That
supports the individual validation-call dispositions, but is not a complete
rendered-page provenance audit of every input to the broad jQuery manipulation
APIs. These four lower-level findings conservatively remain open. Closure needs
complete source-to-sink evidence for the remaining integration surface or a
verified safe-rendering repair; there is no demonstrated application XSS exploit.

## Why the other findings can be dismissed

### Bootstrap: 33 alerts

- Method dispatch uses fixed library calls or component-instance methods. The
  application's only authored Bootstrap data API is fixed navbar collapse
  markup (`_Layout.cshtml:50–53`); no untrusted method-name source was found.
  Some component interfaces only check method existence: that check is **not**
  claimed to be a general allowlist or authorization boundary.
- The event bridge receives component DOM elements, not entered field strings.
- Tooltip/popover string HTML and templates use default sanitation; text is the
  default content mode. Data attributes cannot disable sanitizer options.
  Explicit JavaScript `sanitize:false`, custom sanitizers and DOM-element
  content can bypass those protections, but none is configured by Arkanum.
- **78 probe cases** (39 per asset) exercised real bundled code, fixed navbar
  behavior, method dispatch, sanitizer behavior and unsafe negative controls.

### Validation/unobtrusive: 61 alerts

- Selector arguments were individually traced to DOM nodes/collections from
  parsing, events and validation checks—not entered values used as HTML.
- Error IDs are escaped before regex construction; formatting regexes use an
  array index, and step-type matching tests a fixed short supported-types list.
- Current messages/tags/container options are authored/generated metadata. No
  custom message mutation, remote rule, user-selected tag or regex adapter is
  configured. Server-rendered ModelState errors are not automatically imported
  into JavaScript `errorList` by unobtrusive parsing.
- HTML message APIs remain potentially dangerous. Synthetic custom-message,
  summary and remote-response controls created handler-bearing DOM; this was
  recorded as a conditional weakness, not misreported as safe HTML processing.
- Prototype-sensitive loops exist. Prior contamination can affect them, but
  that is not a demonstrated pollution source. Actual bundled jQuery rejects
  the exercised `__proto__` deep-merge payload; no app untrusted settings/rules
  merge or attacker-to-prototype-write chain was found.
- The email regex was reviewed structurally and exercised with large near-miss
  inputs; results corroborate its disposition, not universal regex safety.

### jQuery core: 46 alerts

- DOM traversal assignments are not writes to `Object.prototype`.
- Deferred/filter/insertion dispatch uses library-controlled tuples, registries
  and function entries, not attacker-supplied code or field-derived method names.
- Three minified `$` alerts name an internal predicate, **not** the jQuery
  constructor. Exact columns and symbol definitions disambiguate the sites.
- Support-fixture HTML uses fixed markup plus an internally generated digits-only
  expando. Attribute handler dispatch has an own-property guard.
- Optional script evaluation, JSONP, HTML loading and wrapping APIs were traced
  to their actual callers; no current app untrusted path/configuration was found.
  This is not a general endorsement of those APIs with arbitrary remote input.

### C# test: alert 8

`UiTypographyAndSettingsTests.cs:8–11` supplies only two fixed source-controlled
font filenames through xUnit `InlineData`. The flagged read at `:27` has no
HTTP/CLI/environment path input, dynamic theory provider or production caller.
Both theory rows ran successfully; **five** tests in this class passed. Changing
or sanitizing those valid literals just to silence the scanner is not warranted.

## Verification and limits

- Parent reran the Bootstrap and jQuery assertion probes against both actual
  assets, reran validation observations and independently asserted their expected
  safe behavior and deliberately unsafe controls.
- Parent repeated the jQuery regex test series and both bounded URL-regex timeout
  tests. Subprocess deadlines prevent the synthetic stress cases running forever.
- DOM tests use Node 22.23.2 and jsdom 26.1.0. Validation hidden-element filtering
  was overridden because jsdom has no layout. Bootstrap uses a no-op
  IntersectionObserver only to construct ScrollSpy. These are not rendered-app,
  live-server, browser exploitability or production-traffic tests.
- No vendor or application source was patched. No Semgrep rule, ignore path,
  coverage diagnostic, scanner version or CodeQL setting was changed.
- The existing **27 partial C# parse diagnostics** remain a scanner coverage
  limitation. Closing false positives does not repair or certify that coverage.
- Re-review dispositions if library bytes, plugin configuration, field/message
  sources, server metadata or application callers change. Actual exploitability,
  upstream fixes and dependency upgrades require separate verification; none was
  invented or assumed in order to close an alert.
