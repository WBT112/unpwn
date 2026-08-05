# AGENTS.md

## Purpose

This file provides repository-level instructions for coding agents and automated contributors working on unpwn.

unpwn is a local-first, open-source account-recovery orchestration tool for users who suspect that their digital identity has been compromised. It is not an antivirus, malware scanner, password manager, or autonomous account-recovery bot.

## Read First

Before changing code or architecture, read the relevant project documents:

- `README.md`
- `docs/VISION.md`
- `docs/ARCHITECTURE.md`
- `docs/UI_FOUNDATION.md`
- `docs/LOCALIZATION.md`
- `docs/RECOVERY_WORKFLOWS.md`
- `docs/DATA_MODEL.md`
- `docs/VAULT_SECURITY.md`
- `docs/TESTING.md`
- `docs/THREAT_MODEL.md`
- `CONTRIBUTING.md`

When instructions conflict, security constraints and explicit architecture decisions take priority. Do not silently reinterpret the product scope.

## Product Boundaries

Preserve these boundaries:

- unpwn guides and tracks account recovery after a suspected compromise.
- unpwn does not detect or remove malware.
- unpwn does not guarantee account recovery.
- unpwn does not bypass CAPTCHA, MFA, identity verification, rate limits, or ownership checks.
- unpwn does not become a general-purpose password manager.
- automation assists bounded actions and remains visible to the user.
- recovery workflows are reviewed in this repository and shipped with releases.
- do not add runtime downloading or execution of third-party provider plugins.

## Technology and Architecture

The planned stack is:

- C#
- .NET LTS
- Avalonia UI
- SQLite
- Playwright for bounded browser assistance

Preserve dependency direction:

- `Unpwn.Core` contains domain models, recovery logic, state machines, prioritization, and progress calculations.
- `Unpwn.Core` must not depend on Avalonia, SQLite, Playwright, operating-system APIs, localization resources, or provider-specific infrastructure.
- `Unpwn.Application` coordinates use cases.
- infrastructure, vault, import, export, automation, and provider concerns remain separate modules.
- platform-specific functionality must be isolated from recovery and workflow logic.
- localization remains a desktop presentation concern and must not leak into canonical domain or persisted data.

Windows is the first target platform, but core components must remain platform-neutral.

## Localization and Presentation Rules

Follow `docs/LOCALIZATION.md` for the authoritative localization contract.

Design every new user-facing surface so additional GUI languages can be added without changing domain, workflow, or vault logic.

- Do not hard-code user-facing text in XAML, code-behind, view models, services, or exception-to-dialog mappings.
- Obtain labels, buttons, menus, dialogs, validation messages, warnings, tooltips, accessibility names, empty states, and progress text through the application localization boundary.
- Use stable descriptive resource keys and parameterized messages; do not assemble translated sentences through string concatenation.
- Keep domain states, audit event types, workflow and action identifiers, error codes, persisted values, and machine-readable formats language-neutral.
- Translate structured codes only at the presentation boundary. Do not persist localized status names or localized error messages as canonical data.
- Use an explicit UI culture for user-visible dates, times, numbers, and percentages. Do not let ambient culture alter security-sensitive parsing or invariant identifiers.
- Preserve deterministic fallback to the complete English source resources when a culture or key is unavailable.
- Avoid fixed text dimensions and layout assumptions based on English string length.
- Keep controls and dialogs resilient to pseudo-localized, longer, and potentially right-to-left text.
- Provider workflow execution must never depend on translated display text.
- New GUI tests should include localization lookup or pseudo-localization coverage where relevant.
- Do not log localization formatting arguments because they may contain user or recovery data.

Localization architecture and acceptance criteria are tracked in Issue #49.

## Security Rules

Treat all account, vault, credential, reset, token, and browser data as sensitive.

Never commit or expose:

- real usernames or email addresses
- passwords
- reset links or reset tokens
- cookies or browser storage state
- MFA secrets or recovery codes
- API tokens, SSH private keys, or live credentials
- personal screenshots or provider traces containing account data

Vault decisions:

- dedicated vault password is the portable primary unlock method
- Argon2id derives the key-encryption key
- AES-256-GCM encrypts sensitive records
- nonce reuse with the same key is forbidden
- old passwords are never persisted
- newly generated credentials may exist only inside the encrypted recovery vault until exported or deleted
- operating-system keychains may later provide optional convenience, not the sole security basis
- do not invent custom cryptographic primitives

Secrets must not appear in logs, exception messages, audit events, telemetry, screenshots, traces, videos, crash reports, localization diagnostics, or test artifacts.

## Recovery Workflow Rules

A workflow must distinguish:

- authenticated password change
- password reset
- manual account recovery

Workflow definitions must model:

- required and optional actions
- dependencies and blocking conditions
- action importance
- supported recovery paths
- automation support level
- completion criteria
- provider URLs and expected origins
- workflow version and verification date

A required action cannot be silently skipped. `NOT_APPLICABLE` requires a recorded reason. Accepted unresolved risks remain visible and prevent the account from being represented as fully secured.

Provider workflows and updates must arrive through pull requests with tests. Do not hard-code provider logic into the UI. User-facing workflow guidance uses localization keys or another translation-safe representation; workflow execution never branches on translated text.

## Testing Strategy

Follow `docs/TESTING.md`.

### Required pull-request testing direction

The blocking CI suite should cover:

- build and formatting or analyzer checks
- unit tests
- workflow schema validation
- semantic workflow validation
- provider contract scenarios
- recovery state-machine and progress tests
- vault and secret-leakage tests
- localization resource and fallback tests
- integration tests against a local synthetic provider
- Playwright tests against that synthetic provider

Normal pull-request tests must not depend on live provider websites or real accounts.

### Synthetic provider

Use a deterministic local ASP.NET Core test provider for browser flows. It should simulate login, re-authentication, password change, password reset, email-link handoff, MFA pause, CAPTCHA pause, expired links, provider errors, unexpected content, and manual-recovery handoff.

Use only synthetic credentials and account identifiers.

### Production versus test browser mode

Production mode:

- browser must be visible
- headless execution must be rejected
- user can pause or abort
- sensitive submission requires explicit authorization
- unexpected content stops the workflow

Test mode:

- headless execution is allowed only against the local synthetic provider
- test mode must be explicit
- target validation must prevent accidental use against live providers
- traces and screenshots may contain synthetic data only

### Live-provider checks

Live-provider smoke checks must be scheduled or manually triggered, read-only, and initially non-blocking for pull requests.

They may validate HTTPS, redirects, expected origins, reachability, and stale verification dates. They must not submit forms, use credentials, trigger reset emails, start MFA or CAPTCHA, or upload browser state and DOM traces.

### Test artifacts

Use recognizable synthetic secret markers such as `UNPWN_TEST_SECRET_...` and scan outputs for leakage.

Artifacts from synthetic tests may include sanitized logs, traces, or screenshots on failure. Live-provider checks must not upload DOM snapshots, cookies, storage state, account identifiers, or sensitive screenshots.

Do not hide flaky product behavior with broad retries.

## Expected Development Checks

Once the solution scaffold exists, the normal local baseline should be equivalent to:

```text
dotnet restore
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Run narrower relevant tests during development, then run the full applicable suite before declaring the work complete.

Do not claim tests passed unless they were actually executed. Document any check that could not be run and why.

## Change Discipline

- Keep changes small, coherent, and reviewable.
- Reference or create an issue for non-trivial work.
- Update architecture and security documentation when behavior changes.
- Add regression tests for defects.
- Avoid unrelated refactoring in a scoped change.
- Do not weaken a security boundary merely to simplify a test.
- Prefer explicit failure over guessing during recovery or automation.
- Preserve compatibility or document migrations for persisted vault and workflow formats.
- Treat resource keys and formatting placeholders as reviewed presentation contracts.

## Completion Checklist

Before finishing a change, verify:

- scope matches the referenced issue
- architecture boundaries remain intact
- no secrets or personal data were added
- relevant tests were added or updated
- failure and blocked paths are tested
- documentation is current
- generated artifacts and logs are secret-safe
- provider URLs and origins are official and justified
- user-facing text is localizable and canonical data remains language-neutral
- resource fallback and formatting placeholders remain valid
- user-visible security claims are accurate and do not overpromise
