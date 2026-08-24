# Desktop end-to-end scenarios

This document is the reviewed inventory for real-process desktop coverage. The harness launches the
production Avalonia application, uses only visible controls with stable automation IDs, and points
provider navigation at a loopback-only synthetic site. It never calls an application service to
advance recovery state. Browser observations remain context, never recovery truth.

## Scenario tiers

| Tier | Purpose | Blocking execution |
| --- | --- | --- |
| `golden` | Complete primary recovery journey, credential handoff, report, and cleanup | Windows 2025, Ubuntu 24.04 LTS, Debian 13 |
| `fast` | UI branches that do not need a native browser | Every pull request on Ubuntu 24.04 LTS |
| `platform` | Native browser startup, cleanup, and non-completion semantics | Windows 2025, Ubuntu 24.04 LTS, Debian 13 |
| `extended` | All deterministic desktop scenarios | Every pull request on Ubuntu 24.04 LTS; this is the baseline for future scheduled expansion |

Ubuntu runs directly on the GitHub-hosted Ubuntu 24.04 image. Debian runs inside the official
`debian:13-slim` container on a separate Ubuntu host; the job verifies `/etc/os-release` before it
builds. These are intentionally distinct userlands and WebKitGTK installations. Windows uses the
Windows Server 2025 hosted image and the installed WebView2 runtime. A missing display, WebKitGTK,
or WebView2 backend fails the scenario rather than skipping it.

## Executable scenario inventory

| ID | Tier | Visible branch and expected transition | CI platforms |
| --- | --- | --- | --- |
| `golden` | golden | trusted device → vault → session → CSV review → category → automatic queue/path → native browser → explicit criteria/completion → credential handoff → final report | Windows, Ubuntu, Debian |
| `safety-stop-and-retry` | fast | not-trusted/unsure guidance → explicit stop; no vault work starts → restart assessment → trusted → vault creation | Ubuntu |
| `vault-wrong-password-and-resume` | fast | lock → rejected password with controlled feedback and cleared input/RAM reference → correct password → resumed next task | Ubuntu |
| `import-correction-and-retry` | fast | ambiguous columns stay in import with disabled confirmation → corrected file → preview → explicit import → accounts | Ubuntu |
| `defer-account` | fast | imported/categorized account → explicit defer → visible unresolved/deferred status without completion | Ubuntu |
| `browser-close-preserves-recovery` | platform | active native browser → explicit close/profile cleanup → same action remains in progress and uncompleted | Windows, Ubuntu, Debian |

Each invocation gets a new temporary data root, vault, browser profile, CSV fixture, and loopback
provider. Artifacts contain a scenario-tagged result, sanitized process output, step/route/control
log, environment and browser-backend metadata, a manifest, and a synthetic-window screenshot on
failure. Vault files, browser profiles, cookies, DOM, entered credentials, and CSV content are never
uploaded. The artifact secret scan is blocking.

## Wider state and transition inventory

Not every branch belongs in an expensive native desktop test. The table below makes the coverage
decision explicit. A new user-visible workflow branch must add an executable desktop scenario or
update this table in review with a stable lower-layer test and a reason.

| Product branch | Permanent coverage | Desktop exception or remaining gap |
| --- | --- | --- |
| trusted, not trusted/unsure, stop, reconsider/retry | `safety-stop-and-retry`; wizard/view-model tests | Reconsider without first stopping is equivalent state-machine coverage and remains a focused view-model test. |
| create, lock, wrong password, unlock, clear secrets, resume | `golden`; `vault-wrong-password-and-resume`; vault lifecycle and presentation tests | Existing-path/no-overwrite, corrupt vault, I/O denial, deletion confirmation, and password change use injected storage/crypto failures that cannot be made deterministic through a native picker. |
| create, pause, resume, archive session | `golden`; application smoke and dashboard tests | Pause/archive conflict and crash-marker recovery remain deterministic service/process-boundary tests; an abrupt OS kill is an extended desktop candidate. |
| automatic CSV mapping, password exclusion, ambiguous mapping, retry, duplicates, malformed/oversized input | `golden`; `import-correction-and-retry`; import unit/property/security tests | Duplicate choices and parser limits stay below desktop because the same UI confirmation delegates to the canonical import result and native-picker behavior adds no state branch. |
| account category suggestions/override and Email → Critical → Unknown → NonCritical queue | `golden`; inventory/planner tests | Multi-account ordering is exhaustively asserted in platform-neutral tests; the desktop journey proves the UI handoff for one synthetic account. |
| defer, lost access, blocked, failed, retry, unresolved risk | `defer-account`; execution/dashboard/application smoke tests | Lost-access and injected persistence conflicts remain lower-layer scenarios. A multi-account desktop retry journey is a documented extended gap. |
| reviewed provider, unknown/manual provider, discovered location, external fallback | `golden`; workflow/location/browser host tests | Native fallback buttons and origin enforcement are headless UI/component tested. Deterministically removing a runtime would contradict the blocking platform job, so missing runtime is verified by fail-fast startup, not a passing fallback scenario. |
| browser start, navigation context, close, cleanup, orphaned cleanup retry | `golden`; `browser-close-preserves-recovery`; browser lifecycle/security tests | Forced crash during native-profile teardown remains an extended desktop candidate; lifecycle tests inject the failure and verify conservative restart handling. |
| credential generation, bounded reveal/copy/assisted insert, confirmation, export/delete | `golden`; credential UI/application/security tests | Clipboard expiry and OS permission failures are deterministic adapter tests; CI never exports a plaintext credential artifact. |
| explicit criteria, completion confirmation, final report | `golden`; execution/completion tests | Browser URL/form/redirect and process restart non-truth are asserted in browser/execution tests; browser close is additionally covered at desktop level. |

## Local execution

Build Release first. Pass `--scenario <id>` to select a row above and use a separate artifact
directory per invocation. Example on Linux:

```shell
xvfb-run --auto-servernum dotnet tools/Unpwn.DesktopE2E/bin/Release/net10.0/Unpwn.DesktopE2E.dll \
  --app "$(realpath src/Unpwn.App/bin/Release/net10.0/Unpwn.App.dll)" \
  --scenario browser-close-preserves-recovery \
  --artifacts "$(pwd)/artifacts/desktop-e2e/browser-close-preserves-recovery"
```

Use an active desktop instead of `xvfb-run` when diagnosing native rendering. Linux requires
WebKitGTK 4.1; Windows requires WebView2. The provider remains local and synthetic in both cases.
