# CodeQL alert review — October 28, 2026

Scope: seven open CodeQL alerts reported against commit
`190050572bad57e8b87b74c49526ebec8a0c7368` in `dw-bshear/Arkanum`.
Line references for vendor files below are relative to that commit; vendor files
were not modified. This is a targeted review, not a certification of all client-side code.

## Alerts 5, 6 and 7: biased cryptographic random selection

Rule: `js/biased-cryptographic-random`.
Original locations: `Arkanum.Web/wwwroot/js/site.js:41`, `:42`, `:44`.
Disposition: confirmed; repaired for version 1.1.1. GitHub alert resolution requires publication and a new CodeQL analysis of the repaired commit.

The password generator reduced a uniform 32-bit random value modulo a range
that need not divide 2^32. This biases character selection and shuffle indices.
The repair uses a power-of-two mask and rejects candidates outside the desired
range, keeping `crypto.getRandomValues` as the randomness source. All three
operations use the same bounded sampler. Length (24), alphabet, and required
character classes are unchanged. No weak-random fallback was added.

Regression command: `node --test tests/password_generator.test.cjs`.
Three tests failed before the repair; all four pass after it. Tests cover rejected
samples, equal representation of accepted indices for all shuffle/character-set
bounds, password policy, and rejection during complete password generation.
This proves the bounded sampler's mapping, not uniformity over all possible
policy-compliant complete passwords.

## Alerts 3 and 4: jQuery feature-detection HTML

Rule: `js/html-constructed-from-input`.
Locations: `Arkanum.Web/wwwroot/lib/jquery/dist/jquery.js:1279` and `:1280`.
Recommended disposition: false positive for the reviewed code.

The interpolated `expando` originates at line 330 as `"jQuery"` followed by digits
from `(version + Math.random()).replace(/\D/g, "")`, and is copied into the
selector-engine closure at line 653. The flagged HTML belongs to the library's
selector feature-detection fixture, not application-generated markup. The value
cannot introduce HTML metacharacters through that initialization. No authored
application mutation of `jQuery.expando` was found. Neither finding establishes
an application-controlled HTML injection path.

Suggested GitHub dismissal comment (one per alert):

> False positive in the reviewed bundled jQuery feature-detection code. The interpolated expando is initialized at jquery.js:330 as the fixed prefix jQuery plus digits only and copied to the local closure at :653. The flagged HTML is an internal selector support fixture, not application/user input. No application mutation of the expando was found. Vendor code remains unchanged.

## Alerts 1 and 2: jQuery Validation element normalization

Rule: `js/unsafe-jquery-plugin`.
Locations: `Arkanum.Web/wwwroot/lib/jquery-validation/dist/jquery.validate.js:689`
(`clean`) and `:1086` (`validationTargetFor`).
Recommended disposition: false positive in the current application integration;
revisit if custom code begins passing input-derived strings as plugin targets.

These helpers wrap their argument in `$()`, which can also interpret strings as
HTML. However, the application's shipped integration initializes validation on
DOM forms using the unobtrusive adapter
(`jquery.validate.unobtrusive.js:127–165`, `:238–252`). Library event handlers pass
DOM elements (`jquery.validate.js:422–454`); `.valid()` iterates elements
(`:126–133`); grouped lookups return elements/jQuery collections (`:501`). The
review found no authored application calls to `.validate()`, `.rules()`,
`.element()`, `$()`, or `jQuery` in `wwwroot/js/*.js` or Razor `.cshtml` files.
No field-value-to-target conversion was identified at either flagged call.

A jsdom 26.1.0 probe loaded the actual bundled jQuery, Validation, and unobtrusive
files, separately for development and minified assets. It parsed a synthetic
required field, checked required failure/success, `.rules()`, `.valid()`,
`validator.element()` and `validator.form()`, including a literal angle-bracket
field value. Instrumented helpers each observed seven DOM-element arguments in
each run. Both runs passed. Hidden-element filtering was disabled in the probe
because jsdom has no layout; this is not a full rendered-app or browser security test.
Probe/dependencies were kept outside the repository; no vendor package was modified.

Suggested GitHub dismissal comment for alert 1:

> False positive for Arkanum's current integration. clean(selector) at jquery.validate.js:689 receives DOM elements (or element collections for grouped lookup), not user-entered field values. Unobtrusive validation initializes DOM forms and built-in validation/event paths pass elements. No authored application call passing an input-derived string as a validation target was found. A probe of both development and shipped minified assets confirmed DOM-element arguments during required-field, rules, element, valid and form validation. Revisit if custom string-target calls are introduced.

Suggested GitHub dismissal comment for alert 2:

> False positive for Arkanum's current integration. validationTargetFor(element) at jquery.validate.js:1086 operates on DOM elements produced by clean() or grouped/radio lookups. Unobtrusive validation and built-in event paths do not pass field values as the target argument. No authored application path supplying an input-derived string was found; probes of development and shipped minified assets confirmed DOM-element arguments during the exercised validation paths. Revisit if custom string-target calls are introduced.

## Verification and outstanding work

- Four Node password-generator tests pass; JavaScript syntax check passes.
- 72 existing Python unit tests pass.
- Vendor probes pass for development and minified assets.
- `git diff --check` passes.
- A subsequent release-preparation search located the pinned .NET SDK 10.0.402
  in a separate tool installation. The complete .NET suite then passed:
  266 tests, zero failures/skips. The earlier default-SDK lookup was incomplete.
- At the time of this source review, no alerts had been dismissed. GitHub alert
  state must be checked after publication/reanalysis; the vendor dispositions
  above remain recommendations until explicitly applied.
