# Testing Strategy

## Purpose

unpwn handles security-sensitive recovery workflows. Tests verify workflow correctness, state transitions, persistence, vault behavior, localization, credential handling, and Recovery Browser boundaries without relying on real accounts or unstable provider pages.

The normal pull-request suite must be deterministic, repeatable, and safe to run in public CI.

## Testing principles

- Use synthetic credentials, account data, browser content, and reset scenarios only.
- Never use real credentials, reset links, cookies, MFA secrets, API tokens, recovery codes, or personal information in tests.
- Never perform destructive or state-changing actions against live provider accounts.
- Keep live-provider checks read-only and separate from blocking pull-request CI.
- Treat logs, exceptions, traces, screenshots, crash output, localization diagnostics, and CI artifacts as potentially public.
- Test semantic failure paths, not only schema validity or happy paths.
- Browser observations are context, not recovery truth: navigation, redirect, close, restart, DOM state, or credential insertion must not complete canonical recovery work.

## Test layers

### 1. Workflow validation

Every provider workflow must pass structural and semantic validation. Validation covers required metadata, stable identifiers, recovery paths, action prerequisites, completion criteria, recovery locations, expected origins, automation-support claims, and localization resources.

Semantic validation rejects unsafe or contradictory definitions such as duplicate IDs, missing prerequisites, cycles, insecure production URLs, unexpected origins, future verification dates, required actions without completion criteria, impossible path combinations, embedded secrets, or translated display text used as control data.

### 2. Recovery contract and domain tests

Provider contract scenarios exercise the Recovery Engine without a browser. Representative scenarios include authenticated password change, password reset, blocked action prerequisites, lost MFA access, expired/unavailable recovery links, manual recovery, required-action failure, blocked work, unresolved-risk acceptance, and access that cannot be restored.

Domain/state-machine tests cover valid and invalid transitions, the exact category queue across culture/restart/incomplete triage, automatic authenticated-change/reset/manual selection, no-safe-path blocking, material fallback history, progress calculation, completion preflight, credential lifecycle, idempotency, and persistence revisions. Required actions cannot be silently skipped. `NOT_APPLICABLE` and risk acceptance retain their required reason/disposition. Selector-shape tests keep browser observations outside the canonical path decision.

Representative domain and parsing tests run under multiple cultures to prove that UI culture does not change canonical behavior.

CSV import boundary tests use small injected limits and cover raw bytes, decoded characters, header/record/field size, columns, rows, preview candidates, retained diagnostics, excluded password fields, cancellation, and rejection before inventory persistence. Limit failures must retain no rejected candidates or imported field content.

### 3. Synthetic provider and Recovery Browser tests

`tests/Unpwn.SyntheticProvider.Tests` provides a local deterministic ASP.NET Core provider on loopback. It exposes explicit scenarios for login, re-authentication, password change/reset, email-link handoff, MFA, CAPTCHA, expired links, provider errors, unexpected content, and manual recovery. The fixture uses synthetic identifiers only and never routes tests to a live provider.

The Recovery Browser is tested through its current managed-browser contracts rather than through a second standalone browser automation state machine. Linux release smoke testing covers both embedded WPE availability selection and the visible app-owned WebKitGTK dialog fallback.

Coverage includes:

- exact expected-origin handling and unsafe-scheme rejection;
- visible origin and recovery-oriented browser controls;
- popup, download, permission, external-protocol, TLS, and unsupported-capability denial;
- opaque unpwn-owned profile paths;
- same-account profile reuse and cross-account isolation;
- browser-data clear, native-resource release, then profile deletion ordering;
- cleanup failure/retry and orphan detection after abnormal termination;
- no automatic resume of stale authenticated browser state;
- explicit checklist persistence without browser-driven completion;
- explicit external-browser fallback when the managed host is unavailable;
- one-command reviewed-provider start with a visible embedded workspace, app-owned Linux dialog, or explicit safe fallback;
- repository-reviewed browser-entry selection over reserved or unsafe URLs from synthetic imports;
- canonical account deferral, restart/resume ordering, and unresolved completion-preflight visibility;
- synthetic provider-reviewed credential insertion with late vault retrieval;
- wrong-origin, changed-page, MFA, CAPTCHA, and email-link stop conditions;
- no form submission or canonical completion caused by credential insertion;
- rejection of generic automatic password-field discovery.

Normal CI never navigates to or mutates live providers.

### 4. Application and UI tests

View-model tests cover locked startup, navigation, command concurrency, validation, persistence failures, runtime language changes, safe-message mapping, guided recovery, account review, browser handoff, credential presentation, and completion review.

Avalonia headless tests cover screen-entry focus, focus after validation, dialogs, live-region metadata, browser/assistant interaction, and important accessibility states without opening a normal desktop window. These tests supplement rather than replace the manual Windows/NVDA and Ubuntu/Orca release checklist in [Desktop Accessibility Acceptance](ACCESSIBILITY_ACCEPTANCE.md).

The post-import navigation scenario drives the visible reviewed-import action against a temporary
encrypted vault. It verifies that persistence succeeds before the canonical transition opens the
rendered Accounts workspace, and that a failed import remains in CSV import without advancing recovery
state. The broader packaged-desktop and platform matrix remains tracked separately from this headless
regression layer.

Security meaning must not depend on color alone, and pseudo-localization/minimum-window testing must keep warnings and primary controls visible or reachable.

### 5. End-to-end smoke journeys

The blocking suite includes an `EndToEndSmoke` category that composes real application services around a temporary encrypted SQLite vault. It covers the trusted-device gate, session creation, canonical CSV import, account-category triage, the category-aware recovery queue, repository-controlled provider workflows, explicit action completion, generated-credential handoff and cleanup, completion review, locking, reopening, and persisted-state validation after an application-style restart.

Negative smoke coverage verifies that an untrusted or uncertain device decision cannot create a sensitive recovery workspace. All paths use synthetic identities and local files.

Run the focused smoke category with:

```pwsh
dotnet test tests/Unpwn.App.Tests/Unpwn.App.Tests.csproj --configuration Release --filter Category=EndToEndSmoke
```

### 6. Packaged desktop end-to-end journey

`tools/Unpwn.DesktopE2E` starts a loopback-only synthetic provider, launches the real `Unpwn.App`
desktop process with isolated temporary application data, and drives the visible recovery journey by
stable automation IDs. It covers the trust gate, default-path vault creation, session creation, CSV
import, account categorization, automatic queue/path selection, the native Managed Recovery Browser,
explicit credential assistance and completion criteria, credential handoff, final report, browser
cleanup, and a clean process exit. It does not call application services to advance user-visible state.

Build the solution first, then run one of these platform commands from the repository root:

```pwsh
# Windows (WebView2 Runtime must be installed)
dotnet tools/Unpwn.DesktopE2E/bin/Release/net10.0/Unpwn.DesktopE2E.dll `
  --app (Resolve-Path src/Unpwn.App/bin/Release/net10.0/Unpwn.App.dll) `
  --artifacts (Join-Path (Get-Location) artifacts/desktop-e2e)
```

```shell
# Linux (WebKitGTK 4.1 plus an active display)
dotnet tools/Unpwn.DesktopE2E/bin/Release/net10.0/Unpwn.DesktopE2E.dll \
  --app "$(realpath src/Unpwn.App/bin/Release/net10.0/Unpwn.App.dll)" \
  --artifacts "$(pwd)/artifacts/desktop-e2e"

# Headless Linux, matching CI:
xvfb-run --auto-servernum dotnet tools/Unpwn.DesktopE2E/bin/Release/net10.0/Unpwn.DesktopE2E.dll \
  --app "$(realpath src/Unpwn.App/bin/Release/net10.0/Unpwn.App.dll)" \
  --artifacts "$(pwd)/artifacts/desktop-e2e"
```

The journey has bounded step and whole-process timeouts. Missing native runtime/display support is a
failure, never a skip or headless substitute. Secret-safe JSON records the logical steps, controls,
exit code, platform, and native backend; failures also capture the current app window. The isolated
vault and browser profile live outside the artifact directory and are deleted after the process exits,
so cookies, vault records, and browser storage are never uploaded. CI runs this same journey on every
pull request and `main` on both Windows and Linux.

### 7. Localization and culture tests

Localization tests verify:

- complete English source resources and deterministic fallback;
- key and placeholder parity for shipped translations;
- exact/neutral culture lookup;
- parameter formatting and plural handling;
- pseudo-localization and long-string behavior;
- localized accessibility names/descriptions;
- invariant parsing of IDs, URLs, origins, workflow versions, and serialized security data;
- import behavior independent of selected GUI language;
- no localized values in canonical domain, audit, vault, workflow, or authorization state.

Missing security-critical resources or translations that alter canonical security meaning are release-blocking defects.

### 8. Scheduled live-provider smoke checks

Live-provider checks are read-only health observations, not account-recovery tests. They may check official recovery URLs, HTTPS, expected-origin redirect chains, plausibility of the final destination, and stale workflow verification dates.

They must not use credentials/cookies, submit forms, trigger reset emails or MFA, create accounts, capture sensitive DOM, or upload browser storage.

The implementation lives in `Unpwn.Automation` with `tools/Unpwn.ProviderSmokeChecks`. `.github/workflows/provider-smoke-checks.yml` runs it on its documented schedule/manual dispatch, separate from pull-request CI. Provider blocking, rate limiting, transient unavailability, and cross-origin redirects remain observations requiring review rather than automatic compromise/workflow conclusions.

### 9. Release verification

Before a supported release:

- run the complete deterministic CI suite;
- validate every shipped workflow definition and fail closed for incompatible persisted execution state;
- manually review changed provider workflows and only update verification metadata after real review;
- review unresolved provider uncertainty;
- verify localization completeness, placeholder parity, pseudo-localization, and minimum-window behavior;
- execute and record the Windows/NVDA and Ubuntu/Orca accessibility checklist;
- review vault, import, export, Recovery Browser, credential-insertion, and interruption boundaries;
- confirm packaging/update behavior without weakening the trusted-device or browser-profile model.

## Pull-request CI

`.github/workflows/ci.yml` is authoritative for the deterministic build/test matrix. It runs restore, Release build, and the complete test suite on Windows and Linux for pushes to `main` and pull requests. Formatting/analyzer verification runs on Linux. Linux also collects Cobertura coverage, merges it, enforces the numeric gate, runs the focused `SecurityRegression` category, verifies the native/unsafe interop allowlist, repeats the NuGet vulnerability gate with an explicit job summary, scans generated artifacts for synthetic secret markers, and uploads short-lived test/coverage artifacts. Windows uploads test artifacts only on failure after the same secret-safety check.

Security-specific gates are intentionally visible rather than being implied by an ordinary green build:

- `.globalconfig` promotes all applicable built-in .NET `Security` analyzer diagnostics to errors while the broader analyzer set remains `Recommended`;
- NuGet audit covers direct and transitive packages and blocks `moderate`-or-higher advisories under the repository warnings-as-errors policy;
- `eng/verify-native-interop.ps1` rejects expansion of `AllowUnsafeBlocks`, P/Invoke/`LibraryImport`, pointer code, or raw-memory APIs outside the reviewed allowlist;
- `dotnet test ... --filter "Category=SecurityRegression"` runs a fast deterministic sentinel suite for vault limits, CSV limits, public-network-only recovery discovery, exact browser origins/schemes, credential lifecycle, Unix plaintext-export permissions, and Linux Recovery Browser profile permissions;
- `.github/workflows/codeql.yml` is the single repository-maintained CodeQL advanced setup and analyzes C# on pull requests, pushes to `main`, and a weekly schedule with `security-extended` queries.

The detailed gate/exception policy and local commands are in [Security CI Gates](SECURITY_GATES.md). Do not solve a security-gate failure by globally suppressing an analyzer, raising the NuGet severity threshold, or broadening a native allowlist without a documented security review.

The Linux coverage gate requires at least 80% line and 80% branch coverage across platform-neutral production assemblies (`Unpwn.Core`, `Unpwn.Application`, `Unpwn.Import`, `Unpwn.Export`, `Unpwn.Vault`, `Unpwn.Providers`, and `Unpwn.Automation`). `Unpwn.App` is validated through view-model, Avalonia headless, integration, and manual accessibility layers rather than the numeric gate. Presentation-independent behavior should not be moved into `Unpwn.App` merely to avoid coverage requirements.

To reproduce the security-specific checks after a Release build:

```pwsh
./eng/verify-native-interop.ps1
dotnet restore unpwn.slnx --force-evaluate
dotnet test unpwn.slnx --configuration Release --no-build --filter "Category=SecurityRegression"
```

To reproduce the coverage check after a Release build:

```pwsh
dotnet test unpwn.slnx --configuration Release --no-build --collect:"XPlat Code Coverage" --results-directory artifacts/test-results -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
dotnet tool restore
dotnet tool run reportgenerator '-reports:artifacts/test-results/**/coverage.cobertura.xml' '-targetdir:artifacts/coverage' '-reporttypes:Cobertura;JsonSummary;TextSummary' '-assemblyfilters:+Unpwn.*;-Unpwn.App'
./eng/verify-coverage.ps1 -CoveragePath artifacts/coverage/Cobertura.xml -MinimumLineRate 0.80 -MinimumBranchRate 0.80
```

Diagnostics tests use recognizable `UNPWN_TEST_SECRET_...` markers. The artifact scan must reject retained test output containing those markers.

## Security and persistence failure coverage

The focused `SecurityRegression` category is deliberately small and deterministic. Property-style cases use fixed seeds and bounded loops rather than large randomized payloads. It is a fast merge gate, not a replacement for the complete suite. Long-running or mutation-based fuzzing, if introduced later, belongs in a separate scheduled workflow and must remain synthetic and resource-bounded.

Persistence-resilience tests inject I/O failures, denied access, conflicting/stale revisions, incompatible/corrupt state, and cancellation around commit boundaries. They assert that failures are not shown as saved, retries are explicit, operation IDs remain idempotent where required, and prepared projections become visible only after successful atomic persistence.

Recovery-boundary tests cover stale process markers, unhandled exceptions, locked-vault recovery, and secret-safe diagnostics. Export tests distinguish file creation from lifecycle-state updates and preserve warnings when plaintext may already exist.

A failing secret-leak, unsafe-origin, unauthenticated-vault, invalid workflow, nonce-reuse, browser-completion, localization-semantic, persistence-integrity, dependency-audit, native-boundary, Security-analyzer, or CodeQL finding is a security failure to investigate rather than a flaky test to retry away.

## Test data and artifacts

Use recognizable synthetic data. The canonical import fixtures live under `samples/import/` and cover normal recovery data, password-manager-style mapping/secret-column exclusion, duplicate handling, and deterministic edge cases.

Synthetic values must never be accepted by production code as implicit evidence that test mode is active; test-only browser behavior requires explicit configuration and loopback validation.

Do not retain browser profiles, cookies, DOM snapshots, real account identifiers, or secrets in CI artifacts. Screenshots/traces are only appropriate when a test layer can guarantee synthetic content and the relevant artifact policy explicitly permits them.

## Flaky tests

Do not solve flaky tests with broad retries. First identify uncontrolled time, randomness, shared mutable state, external network dependency, race conditions, ambient culture leakage, platform layout assumptions, or insufficient synthetic-provider controls. Retries are reserved for narrowly understood infrastructure failures and must not hide deterministic defects.
