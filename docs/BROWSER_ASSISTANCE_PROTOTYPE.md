# Bounded Browser Assistance Prototype

## Purpose and scope

The Issue #16 prototype evaluates Playwright for one controlled action: filling and submitting a new generated password on the local synthetic provider's authenticated password-change page. It does not automate a real provider, discover arbitrary form fields, complete a recovery action, or rotate a password without the user.

The application contract lives in `Unpwn.Application`; the Playwright adapter and its page object live in `Unpwn.Automation`. The page object uses canonical `data-testid` and `data-unpwn-*` attributes. It never uses translated labels as selectors or as control data.

## Execution boundary

Every launch has an explicit execution mode.

Production mode:

- rejects headless launch;
- currently accepts only a loopback controlled page, because no real-provider adapter has been reviewed;
- permits pause, resume, or abort while the browser session is active;
- requires a fresh structured user authorization before reading or submitting a generated credential.

Synthetic test mode:

- permits headless launch only against a loopback target;
- aborts redirects and subresource requests that leave HTTP(S) loopback before they reach the network;
- requires the caller to declare synthetic credentials before test artifacts could be enabled;
- is exercised by CI against the deterministic ASP.NET Core synthetic provider.

Screenshots and tracing are disabled by the prototype. The configuration guard additionally rejects enabling artifacts outside explicit synthetic test mode with synthetic credentials. This preserves a safe boundary if synthetic-only artifacts are added later.

## Workflow behavior

The adapter inspects the known page before requesting the credential and inspects it again after authorization. It reads the generated password from the unlocked vault through a disposable `CredentialSecretLease` only when the expected fields and submit control are still present.

The workflow pauses without reading the credential when it sees an explicit MFA, CAPTCHA, or email-link handoff marker. Missing, duplicated, or changed controls produce a structured `ManualGuidanceRequired` result. Playwright exception messages, page content, URLs, and secret values are not copied into results or diagnostics.

A successful synthetic form submission reports only that the browser submission occurred. It does not mark a canonical recovery action complete. The user must still verify the provider-defined completion criteria through the normal recovery workflow.

## Findings and rollout decision

The prototype shows that Playwright can enforce a useful boundary between generic workflow control and a small page object while delaying vault access until explicit authorization. The deterministic stop conditions are maintainable on a controlled page.

The prototype does **not** justify automation against real providers yet. Real provider markup, navigation, re-authentication, consent, and anti-abuse flows change independently and introduce a materially larger maintenance and security surface. Before any real-provider implementation, a separate issue and review must provide:

- a single repository-reviewed provider and action;
- a dedicated page object with exact expected origins and page-state evidence;
- localized visible authorization and manual-fallback UI;
- a user-visible headed-browser lifecycle with pause and abort controls;
- a threat review covering browser profiles, downloads, redirects, popups, and credential exposure;
- synthetic contract coverage for every observed stop and failure state;
- an explicit decision about whether the measured user benefit warrants ongoing provider maintenance.

Until those conditions are met, browser assistance remains synthetic-only. Recovery-location discovery followed by visible manual guidance is the supported path for real providers.

## Verification

The synthetic-provider suite covers the production headless guard, non-loopback rejection, artifact policy, authorization boundary, credential-read timing, successful submission, MFA/CAPTCHA/email-link pauses, unexpected content, manual fallback, and pause/resume/abort behavior. CI installs the package-matched Chromium build and runs the browser tests headlessly on Linux and Windows.
