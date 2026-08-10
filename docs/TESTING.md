# Testing Strategy

## Purpose

unpwn handles security-sensitive recovery workflows. Tests must verify workflow correctness, state transitions, progress reporting, vault behavior, localization behavior, and browser-assistance boundaries without relying on real accounts or unstable provider websites.

The normal pull-request test suite must be deterministic, repeatable, and safe to run in public CI.

## Testing Principles

- Use synthetic credentials and synthetic account data only.
- Do not use real accounts, reset links, cookies, MFA secrets, API tokens, or personal information in tests.
- Do not make destructive changes to live provider accounts.
- Keep live-provider checks read-only and separate from the blocking pull-request suite.
- Treat logs, traces, screenshots, crash output, localization diagnostics, and CI artifacts as potentially public.
- A workflow is not considered tested only because its file matches a schema. Its recovery logic and failure paths must also be exercised.
- A language is not considered supported only because a resource file compiles. Fallback, formatting, layout, accessibility, and security meaning must also be tested.

## Test Layers

### 1. Structural workflow validation

Every workflow definition must pass a machine-readable schema or equivalent validator.

Structural validation should cover:

- required metadata
- workflow and provider identifiers
- supported account type
- workflow version
- verification date
- known action types
- known recovery paths
- known automation support levels
- required and optional actions
- prerequisite references
- recovery locations
- completion criteria
- referenced localization keys where user-facing workflow guidance is defined

### 2. Semantic workflow validation

Semantic validators must reject definitions that are structurally valid but unsafe or contradictory.

Checks should include:

- duplicate workflow or action identifiers
- missing prerequisite targets
- cyclic action dependencies
- insecure non-HTTPS URLs unless explicitly justified for a local test fixture
- unexpected provider origins
- verification dates in the future
- required actions without completion criteria
- impossible recovery-path combinations
- automation claims that exceed the implemented capability
- embedded secrets or personal test data
- translated display text used as control data

Validation failures should identify the workflow, action, and rule that failed through structured codes. Presentation tests separately verify the localized diagnostic text.

### 3. Recovery contract tests

Each provider workflow must define deterministic scenarios that exercise the Recovery Engine without a browser.

Minimum scenario coverage should include:

- authenticated password change is available
- password reset through a secured primary email account
- password reset blocked by an unsecured dependency
- MFA device unavailable
- reset link expired or unavailable
- manual account recovery required
- a required action fails
- a required action is blocked
- the user accepts an unresolved risk
- account access cannot be restored

Contract tests should verify:

- selected recovery path
- generated action set
- action order and prerequisites
- blocking conditions
- account status
- unresolved-risk handling
- critical-account readiness
- weighted progress calculations

Contract tests use canonical IDs and values and must pass independently of the selected UI culture.

### 4. State-machine and domain tests

Unit tests must cover valid and invalid transitions for sessions, accounts, and recovery actions.

Required actions must not be silently skipped. `NOT_APPLICABLE` must require a recorded reason. Audit events must not contain secret fields or localized summaries.

Run representative domain and parsing tests under more than one ambient culture to prove that UI culture does not change canonical behavior.

### 5. Synthetic provider integration tests

Browser assistance must be tested against a local deterministic test provider, not against live Google, Microsoft, GitHub, or other third-party websites.

The synthetic provider should be a small local ASP.NET Core application that can simulate:

- login and re-authentication
- authenticated password change
- forgot-password flow
- reset-link handoff
- MFA pause
- CAPTCHA pause
- expired reset link
- unexpected page content
- provider error
- network delay or interruption
- manual-recovery handoff

The initial deterministic harness lives in `tests/Unpwn.SyntheticProvider.Tests`. It starts a local ASP.NET Core app on loopback with explicit scenario query parameters for login, re-authentication, password change, password reset, email-link handoff, MFA pause, CAPTCHA pause, expired links, provider errors, unexpected content, and manual-recovery handoff. The harness must keep using synthetic identifiers only and must not route tests to live providers.

The synthetic provider must expose explicit scenario controls so tests do not depend on timing, randomness, or external services.

### Application-shell view-model tests

The desktop presentation layer must be testable without opening native windows.

View-model tests cover locked startup, route navigation, global lock visibility, constructor-injected services, busy and cancellation states, repeated command execution, stable error-code mapping, runtime language changes, and localized safe-message fallback.

Visual-state tests verify that blocked, failed, and unresolved-risk states have distinct localized text and symbols in addition to color.

### 6. Localization and culture tests

Localization tests follow [Localization and Multilingual GUI](LOCALIZATION.md).

Required coverage includes:

- every referenced key exists in complete English source resources
- exact-culture lookup, such as `de-DE`
- neutral-parent fallback, such as `de`
- fallback to English
- visible missing-key behavior when the English key is absent
- resource-key parity for every shipped translation
- preservation of formatting placeholders
- parameterized messages with the selected UI culture
- zero, one, and other plural variants where applicable
- runtime language switching and view-model refresh
- localized accessibility names and descriptions
- date, time, number, and percentage formatting
- invariant parsing of GUIDs, URLs, origins, workflow versions, and serialized data under multiple UI cultures
- import behavior that does not change with the GUI language
- no localized values in canonical domain, audit, vault, or workflow state
- no direct user-facing string literals in presentation code where a practical analyzer or repository convention can enforce this

Pseudo-localization should:

- visibly delimit every resource
- expand text length
- exercise accented or non-ASCII characters
- preserve placeholders
- expose concatenated-sentence and clipping defects

At the documented minimum window size, critical warnings, confirmation consequences, blocked states, and primary actions must remain visible or reachable through scrolling.

Missing default resources, broken placeholders, absent security-critical warnings, or localization behavior that changes canonical security logic are release-blocking.

### 7. Playwright test mode

Production browser assistance and CI browser testing have different execution rules.

Production mode:

- browser window must be visible
- headless execution is rejected
- the user can pause or abort
- sensitive submission requires explicit authorization
- unexpected page content stops the workflow

Test mode:

- headless execution is allowed
- target must be the local synthetic provider
- credentials and account data must be synthetic
- no network access to live providers is required

The production guard that rejects headless mode must itself be covered by a test.

Browser tests use stable automation IDs or canonical selectors rather than translated visible labels. A small set of end-to-end tests should still run with a secondary or pseudo-localized culture to verify displayed guidance and layout.

### 8. Scheduled live-provider smoke checks

Live-provider checks are read-only health checks. They are not end-to-end account-recovery tests.

They may verify:

- official recovery URLs are reachable
- HTTPS is used
- redirect chains remain within expected origins
- the final destination is plausible
- a workflow verification date has become stale

They must not:

- use credentials or cookies
- submit login, password-change, or reset forms
- trigger reset emails, MFA challenges, or CAPTCHA
- create accounts
- upload browser storage state
- capture sensitive DOM content

These checks should run on a schedule and through manual dispatch. They should initially report warnings rather than block normal pull requests because provider bot protection, regional variants, and transient outages can produce false alarms.

Smoke checks use canonical URLs and origins and are independent of the selected GUI language.

The implementation lives in `Unpwn.Automation` with a small repository tool under
`tools/Unpwn.ProviderSmokeChecks`. `.github/workflows/provider-smoke-checks.yml` runs it weekly and
through manual dispatch; it is deliberately not a pull-request trigger. Requests use `GET` with no
body, credentials, cookies, or referrer, and redirects are followed manually only while every hop
remains HTTPS and within the location's exact expected-origin list.

The job writes an issue-ready Markdown table to the GitHub step summary and warning annotations to
the log. Redirect diagnostics contain origins only; exception messages, response bodies, DOM data,
screenshots, traces, cookies, and browser storage are neither retained nor uploaded. Provider blocking,
rate limiting, transient unavailability, and unexpected cross-origin redirects remain distinct
observations requiring manual review rather than being reported as confirmed workflow defects.

### 9. Release verification

Before release:

- run the complete deterministic CI suite
- validate every shipped workflow definition
- verify workflow-version compatibility with persisted sessions
- review changed provider workflows manually
- update `VerifiedAt` only after an actual review
- record unresolved provider uncertainties
- verify English resource completeness
- verify key and placeholder parity for every shipped translation
- review security-sensitive translations for meaning
- run pseudo-localization and minimum-window checks

## Pull-Request CI

The current baseline is implemented in `.github/workflows/ci.yml`. It performs restore, Release build, and the complete test suite on Windows and Linux for pushes to `main` and pull requests. Formatting and analyzer verification run once on Linux. Cobertura coverage collection, synthetic-secret artifact scanning, and the normal successful artifact upload also run on Linux. Windows uploads test artifacts only when its build or tests fail. Successful retained artifacts use the configured short retention period.

Diagnostics tests use recognizable `UNPWN_TEST_SECRET_...` markers. The application diagnostic boundary records a bounded operation, stable event ID, static message, and exception type only. It returns a new static-message exception for propagation; source exception messages, inner exceptions, stack traces, localization formatting arguments, and imported values are deliberately excluded because they may contain secrets.

The blocking pull-request suite should eventually run:

1. restore dependencies
2. build with warnings treated according to project policy
3. formatting and analyzer checks
4. unit tests
5. workflow structural validation
6. workflow semantic validation
7. provider contract tests
8. recovery state-machine and progress tests
9. localization key, fallback, formatting, and culture-invariance tests
10. pseudo-localization or long-string UI checks
11. synthetic-provider integration tests
12. Playwright tests against the local provider
13. checks that representative secrets do not appear in logs, database files, exceptions, localization diagnostics, traces, screenshots, or uploaded artifacts

No pull-request job should require access to a real provider account.

## Platform Matrix

The core solution should build and run unit tests on Windows and at least one non-Windows runner.

Localization lookup and culture-invariance tests run on both supported CI operating systems where practical. Platform-specific differences in available cultures or fonts must be documented and must not produce silent security-text loss.

Browser-assistance tests may use a narrower supported runner matrix if required, but platform-specific limitations must be documented and must not introduce dependencies into `Unpwn.Core`.

## Test Data

Use recognizable synthetic secret markers, for example values beginning with `UNPWN_TEST_SECRET_`, so tests can scan logs, files, and artifacts for accidental leakage.

The repository-controlled fixtures under `samples/import/` are the canonical manual recovery smoke-test data set. `generic-recovery-sample.csv` covers every shipped provider workflow and recovery path, `bitwarden-recovery-sample.csv` exercises password-manager-style mapping and secret-column exclusion, and `import-edge-cases.csv` provides deterministic duplicate and row-diagnostic cases. Their companion scenario matrix documents post-import roles, dependencies, blocked work, lost access, and unresolved-risk setup without encoding unsupported fields into CSV.

Synthetic reset tokens, credentials, account identifiers, localization arguments, and imported values must never be accepted by production code as trusted test-mode indicators. Test mode must be selected through explicit application configuration and restricted target validation.

Localization tests use synthetic non-secret values and must not place secrets into resource files, pseudo-localized screenshots, or formatting-failure output.

## CI Artifacts

For local synthetic-provider failures, CI may upload:

- test results
- sanitized logs
- Playwright traces
- screenshots
- pseudo-localization screenshots
- videos where justified

Artifacts must contain synthetic data only and should be retained for the shortest useful period.

For live-provider smoke checks, do not upload:

- screenshots after any input
- DOM snapshots or Playwright traces
- cookies
- local storage or session storage
- browser profiles
- account identifiers

## Flaky Tests

Do not solve flaky tests with broad retries.

First identify whether the failure is caused by:

- uncontrolled time
- randomness
- shared mutable state
- network dependency
- race conditions
- ambient culture leakage
- platform font or layout assumptions
- insufficient synthetic-provider controls

Retries may be used only for narrowly understood infrastructure failures and must not hide deterministic product defects.

## Security Failures

A test that detects secret leakage, invalid workflow validation, nonce reuse, unauthenticated vault data, unsafe production automation mode, missing security-critical resources, translated control data, or culture-dependent canonical parsing is release-blocking.
