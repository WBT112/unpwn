# Bounded Browser Assistance Prototype

## Purpose and scope

The Issue #16 prototype evaluates Playwright for one controlled action: filling and submitting a new generated password on the local synthetic provider's authenticated password-change page. It does not automate a real provider, discover arbitrary form fields, complete a recovery action, or rotate a password without the user.

The application contract lives in `Unpwn.Application`; the Playwright adapter and its page object live in `Unpwn.Automation`. The page object uses canonical `data-testid` and `data-unpwn-*` attributes. It never uses translated labels as selectors or as control data.

This document records the original standalone-browser research. The managed Recovery Browser introduced by Issues #92-#94 is now the normal embedded provider work surface. Issue #95 reuses the security lessons from this prototype for an optional in-context insertion contract, but does **not** make Playwright the Recovery Browser or the source of recovery truth.

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

## Managed Recovery Browser follow-up

Issue #95 adds a narrower credential-insertion contract directly to the embedded Recovery Browser. It preserves the important prototype boundaries while deliberately reducing automation:

- manual Reveal/Copy is the default;
- an insertion adapter must be repository-controlled for one provider/action and exact expected origins;
- the current page is inspected before the generated credential is read from the vault;
- wrong origin, changed/missing controls, MFA, CAPTCHA, and email-link handoff stop without secret retrieval;
- the exact contract is re-checked immediately before field insertion;
- the embedded adapter sets only the reviewed new-password and confirmation fields;
- it does **not** submit the form;
- successful insertion can be recorded as credential `Used`, but it cannot confirm the credential works and cannot complete the recovery action.

The first managed-browser insertion adapter is synthetic-test only. It exists to prove the contract against deterministic content, not to enable generic DOM automation. Unsupported/generic providers remain manual Reveal/Copy, and real providers require a separate reviewed adapter before assisted insertion can appear.

Playwright remains useful for bounded research and synthetic-provider testing. It is not required for basic Recovery Browser navigation or manual credential handoff, and no code should couple canonical recovery state to Playwright page observations.

## Findings and rollout decision

The prototype shows that Playwright can enforce a useful boundary between generic workflow control and a small page object while delaying vault access until explicit authorization. The deterministic stop conditions are maintainable on a controlled page.

The prototype does **not** justify automation against real providers yet. Real provider markup, navigation, re-authentication, consent, and anti-abuse flows change independently and introduce a materially larger maintenance and security surface. Before any real-provider implementation, a separate issue and review must provide:

- a single repository-reviewed provider and action;
- a dedicated page object or embedded-browser insertion adapter with exact expected origins and page-state evidence;
- localized visible authorization and manual-fallback UI;
- a user-visible browser lifecycle with pause/close controls;
- a threat review covering browser profiles, downloads, redirects, popups, and credential exposure;
- synthetic contract coverage for every observed stop and failure state;
- an explicit decision about whether the measured user benefit warrants ongoing provider maintenance.

Until those conditions are met, provider-specific automatic insertion remains unavailable. The managed Recovery Browser plus visible manual guidance and Reveal/Copy is the supported real-provider path.

## Verification

The original synthetic-provider suite covers the production headless guard, non-loopback rejection, artifact policy, authorization boundary, credential-read timing, successful submission, MFA/CAPTCHA/email-link pauses, unexpected content, manual fallback, and pause/resume/abort behavior. CI installs the package-matched Chromium build and runs those Playwright tests headlessly on Linux and Windows.

The embedded Recovery Browser tests separately cover the synthetic insertion contract, wrong-origin rejection, changed page evidence, MFA/CAPTCHA/email-link stop conditions, insertion without submission, and the absence of generic production DOM insertion. Existing secret-marker scans continue to gate generated CI artifacts.
