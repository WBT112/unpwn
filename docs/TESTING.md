# Testing Strategy

## Purpose

unpwn handles security-sensitive recovery workflows. Tests must verify workflow correctness, state transitions, progress reporting, vault behavior, and browser-assistance boundaries without relying on real accounts or unstable provider websites.

The normal pull-request test suite must be deterministic, repeatable, and safe to run in public CI.

## Testing Principles

- Use synthetic credentials and synthetic account data only.
- Do not use real accounts, reset links, cookies, MFA secrets, API tokens, or personal information in tests.
- Do not make destructive changes to live provider accounts.
- Keep live-provider checks read-only and separate from the blocking pull-request suite.
- Treat logs, traces, screenshots, crash output, and CI artifacts as potentially public.
- A workflow is not considered tested only because its file matches a schema. Its recovery logic and failure paths must also be exercised.

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

Validation failures should identify the workflow, action, and rule that failed.

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

### 4. State-machine and domain tests

Unit tests must cover valid and invalid transitions for sessions, accounts, and recovery actions.

Required actions must not be silently skipped. `NOT_APPLICABLE` must require a recorded reason. Audit events must not contain secret fields.

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

The synthetic provider must expose explicit scenario controls so tests do not depend on timing, randomness, or external services.

### 6. Playwright test mode

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

### 7. Scheduled live-provider smoke checks

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

### 8. Release verification

Before release:

- run the complete deterministic CI suite
- validate every shipped workflow definition
- verify workflow-version compatibility with persisted sessions
- review changed provider workflows manually
- update `VerifiedAt` only after an actual review
- record unresolved provider uncertainties

## Pull-Request CI

The blocking pull-request suite should eventually run:

1. restore dependencies
2. build with warnings treated according to project policy
3. formatting and analyzer checks
4. unit tests
5. workflow structural validation
6. workflow semantic validation
7. provider contract tests
8. recovery state-machine and progress tests
9. synthetic-provider integration tests
10. Playwright tests against the local provider
11. checks that representative secrets do not appear in logs, database files, exceptions, traces, or uploaded artifacts

No pull-request job should require access to a real provider account.

## Platform Matrix

The core solution should build and run unit tests on Windows and at least one non-Windows runner.

Browser-assistance tests may use a narrower supported runner matrix if required, but platform-specific limitations must be documented and must not introduce dependencies into `Unpwn.Core`.

## Test Data

Use recognizable synthetic secret markers, for example values beginning with `UNPWN_TEST_SECRET_`, so tests can scan logs, files, and artifacts for accidental leakage.

Synthetic reset tokens, credentials, and account identifiers must never be accepted by production code as trusted test-mode indicators. Test mode must be selected through explicit application configuration and restricted target validation.

## CI Artifacts

For local synthetic-provider failures, CI may upload:

- test results
- sanitized logs
- Playwright traces
- screenshots
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
- insufficient synthetic-provider controls

Retries may be used only for narrowly understood infrastructure failures and must not hide deterministic product defects.

## Security Failures

A test that detects secret leakage, invalid workflow validation, nonce reuse, unauthenticated vault data, or an unsafe production automation mode is release-blocking.
