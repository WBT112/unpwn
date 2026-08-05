# AGENTS.md

## Purpose

This file provides repository-level instructions for coding agents and automated contributors working on unpwn.

unpwn is a local-first, open-source account-recovery orchestration tool for users who suspect that their digital identity has been compromised. It is not an antivirus, malware scanner, password manager, or autonomous account-recovery bot.

## Read First

Before changing code or architecture, read the relevant project documents:

- `README.md`
- `docs/VISION.md`
- `docs/ARCHITECTURE.md`
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
- `Unpwn.Core` must not depend on Avalonia, SQLite, Playwright, operating-system APIs, or provider-specific infrastructure.
- `Unpwn.Application` coordinates use cases.
- infrastructure, vault, import, export, automation, and provider concerns remain separate modules.
- platform-specific functionality must be isolated from recovery and workflow logic.

Windows is the first target platform, but core components must remain platform-neutral.

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

Secrets must not appear in logs, exception messages, audit events, telemetry, screenshots, traces, videos, crash reports, or test artifacts.

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

Provider workflows and updates must arrive through pull requests with tests. Do not hard-code provider logic into the UI.

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
- user-visible security claims are accurate and do not overpromise
