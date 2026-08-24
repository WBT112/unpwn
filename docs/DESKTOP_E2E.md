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
| `release` | Published artifacts, abrupt restart, external OS interaction, and compositor-sensitive evidence | Scheduled/manual Release confidence workflow plus the recorded manual Wayland gate |

Ubuntu runs directly on the GitHub-hosted Ubuntu 24.04 image. Debian runs inside the official
`debian:13-slim` container on a separate Ubuntu host; the job verifies `/etc/os-release` before it
builds. These are intentionally distinct userlands and WebKitGTK installations. Windows uses the
Windows Server 2025 hosted image and the installed WebView2 runtime. A missing display, WebKitGTK,
or WebView2 backend fails the scenario rather than skipping it.

## Executable scenario inventory

| ID | Tier | Visible branch and expected transition | CI platforms |
| --- | --- | --- | --- |
| `golden` | golden | trusted device → default-location vault → automatic-name session → progressively disclosed CSV review → single-account category task → automatic queue/path → native browser → explicit criteria/completion → stage-relevant credential handoff → automatic completion preflight/final report; every normal transition asserts its stable primary-action presentation | Windows, Ubuntu, Debian |
| `safety-stop-and-retry` | fast | not-trusted/unsure guidance → explicit stop; no vault work starts → restart assessment → trusted → vault creation | Ubuntu |
| `vault-wrong-password-and-resume` | fast | lock → rejected password with controlled feedback and cleared input/RAM reference → correct password → resumed next task | Ubuntu |
| `import-correction-and-retry` | fast | ambiguous columns stay in import with disabled confirmation → corrected file → preview → explicit import → accounts | Ubuntu |
| `defer-account` | fast | imported/categorized account → explicit defer → visible unresolved/deferred status without completion | Ubuntu |
| `browser-close-preserves-recovery` | platform | active native browser → explicit close/profile cleanup → same action remains in progress and uncompleted | Windows, Ubuntu, Debian |
| `reviewed-browser-step-hierarchy` | platform | reviewed GitHub workflow → one primary task with collapsed details → native managed browser → explicit close/cleanup | Windows, Ubuntu, Debian |
| `browser-startup-failure-fallback` | fast | generic Bitwarden workflow → deterministic pre-host startup failure → retry remains primary and validated external fallback becomes visible without navigation | Windows, Ubuntu, Debian |
| `multi-account-transition` | extended | automatic email/critical categories plus one manual review → email defer → critical provider failure/fallback → next account → account-bound browser rebind/cleanup → deferred work returns → completion preflight retains open work | Ubuntu PR; Windows/Linux release |
| `interruption-mid-recovery` | release | in-progress action → browser closes without completion → outer harness kills the process → startup warning/unlock → identical in-progress action resumes without fabricated criteria or browser truth | Windows/Linux release |
| `interruption-active-browser` | release | active account-bound browser → outer harness kills the complete process tree → orphan marker is detected after restart → explicit cleanup → identical in-progress action resumes without restoring authenticated truth | Windows/Linux release |
| `external-black-box` | release | published Linux app → external AT-SPI process focuses and activates the trusted-device/vault/import/recovery path → browser surface is externally observable → confirmation cancel/confirm → normal keyboard close | Linux release |

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
| create, pause, resume, archive session | `golden`; both `interruption-*` scenarios; application smoke and dashboard tests | Pause/archive conflicts remain deterministic service tests; bounded real-process termination is covered at the release tier. |
| automatic CSV mapping, password exclusion, ambiguous mapping, retry, duplicates, malformed/oversized input | `golden`; `import-correction-and-retry`; import unit/property/security tests | Duplicate choices and parser limits stay below desktop because the same UI confirmation delegates to the canonical import result and native-picker behavior adds no state branch. |
| account category suggestions/override and Email → Critical → Unknown → NonCritical queue | `golden`; `multi-account-transition`; inventory/order tests | The representative desktop journey deliberately covers one automatic email, one automatic critical, and one manual category decision; the complete ordering cross-product remains platform-neutral. |
| defer, lost access, blocked, failed, retry, unresolved risk | `defer-account`; `multi-account-transition`; execution/dashboard/application smoke tests | Lost-access and injected persistence conflicts remain lower-layer scenarios; the desktop journey proves that a provider failure/fallback and repeated deferral do not strand the visible queue. |
| reviewed provider, unknown/manual provider, discovered location, external fallback | `reviewed-browser-step-hierarchy`; `browser-startup-failure-fallback`; `golden`; workflow/location/browser host tests | The deterministic failure is limited to an explicit validated E2E scenario and runs before native host/session creation or navigation; no live provider page is automated. Unsafe/rejected-origin branches remain exhaustive lower-layer tests. |
| browser start, navigation context, close, cleanup, orphaned cleanup retry | `golden`; `reviewed-browser-step-hierarchy`; `browser-close-preserves-recovery`; both `interruption-*` scenarios; browser lifecycle/security tests | The release tier now kills the real app at both bounded checkpoints; random process-kill fuzzing remains intentionally out of scope. |
| credential generation, bounded reveal/copy/assisted insert, confirmation, export/delete | `golden`; credential UI/application/security tests | Clipboard expiry and OS permission failures are deterministic adapter tests; CI never exports a plaintext credential artifact. |
| explicit criteria, completion confirmation, final report | `golden`; execution/completion tests | Browser URL/form/redirect and process restart non-truth are asserted in browser/execution tests; browser close is additionally covered at desktop level. |

## Local execution

Build Release first:

```shell
dotnet restore unpwn.slnx
dotnet build unpwn.slnx --configuration Release --no-restore
```

Pass `--scenario <id>` to select a row above and use a separate artifact directory per invocation.
Windows requires the installed WebView2 Runtime:

```pwsh
dotnet tools/Unpwn.DesktopE2E/bin/Release/net10.0/Unpwn.DesktopE2E.dll `
  --app (Resolve-Path src/Unpwn.App/bin/Release/net10.0/Unpwn.App.dll) `
  --scenario golden `
  --artifacts (Join-Path (Get-Location) artifacts/desktop-e2e/golden)
```

Linux requires WebKitGTK 4.1 and an active display. The Ubuntu/Debian package used by CI is
`libwebkit2gtk-4.1-0`; headless execution additionally requires `xvfb`:

```shell
xvfb-run --auto-servernum dotnet tools/Unpwn.DesktopE2E/bin/Release/net10.0/Unpwn.DesktopE2E.dll \
  --app "$(realpath src/Unpwn.App/bin/Release/net10.0/Unpwn.App.dll)" \
  --scenario browser-close-preserves-recovery \
  --artifacts "$(pwd)/artifacts/desktop-e2e/browser-close-preserves-recovery"
```

Use an active desktop instead of `xvfb-run` when diagnosing native rendering. Linux requires
the same managed browser boundary whether Avalonia selects WPE or the app-owned WebKitGTK dialog.
The provider remains local and synthetic on every platform.

## Published release artifact

`.github/workflows/release-confidence.yml` is the release-level system boundary. It publishes the
actual application as self-contained, multi-file output for the explicit `linux-x64` and `win-x64`
RIDs with symbols disabled. Until an installer/package format is selected, this RID-specific publish
directory is the canonical release candidate; the workflow does not invent a test-only installer.

The candidate is copied to a clean temporary installation directory outside the checkout. The outer
harness launches `Unpwn.App`/`Unpwn.App.exe` directly from there with an unrelated working directory,
creates and opens a temporary vault, reaches the recovery workspace, and starts the native Managed
Recovery Browser. A reviewable manifest records every relative path, byte length, and SHA-256 digest.
The gate rejects test/source/data remnants including PDBs, CSV files, vaults, databases, browser
profiles, markers, samples, and known synthetic-secret markers in reviewable text files. Managed
assets travel with the publish output; Windows still requires the supported WebView2 runtime and
Linux declares WebKitGTK 4.1 as an OS prerequisite.

To reproduce the Linux artifact smoke:

```shell
publish_root="$(mktemp -d /tmp/unpwn-publish-XXXXXX)"
evidence_root="$(mktemp -d /tmp/unpwn-release-evidence-XXXXXX)"
dotnet restore src/Unpwn.App/Unpwn.App.csproj --runtime linux-x64
dotnet publish src/Unpwn.App/Unpwn.App.csproj --configuration Release \
  --runtime linux-x64 --self-contained true --no-restore \
  -p:DebugSymbols=false -p:DebugType=None -p:PublishSingleFile=false \
  --output "$publish_root"
dotnet build tools/Unpwn.DesktopE2E/Unpwn.DesktopE2E.csproj --configuration Release
dotnet tools/Unpwn.DesktopE2E/bin/Release/net10.0/Unpwn.DesktopE2E.dll \
  --app "$publish_root/Unpwn.App" --published-root "$publish_root" \
  --scenario golden --artifacts "$evidence_root"
```

Use `win-x64` and `Unpwn.App.exe` for Windows. The scheduled/manual workflow additionally runs
`multi-account-transition`, `interruption-mid-recovery`, and `interruption-active-browser` against
the same clean installation.

## External desktop boundary

`eng/external-desktop-smoke.py` is deliberately small and separate from the application process. On
the scheduled/manual Linux release job it uses AT-SPI automation identifiers and accessibility
actions for focus, text input, toggles, and activation. A controlled Openbox/Xvfb session lets
`xdotool` activate the process-owned main window and send its final normal `Alt+F4`. It never uses coordinates, image matching, application services,
or broad retries. The CSV picker uses the existing explicit loopback E2E fixture handoff because
native file-picker automation is the unstable boundary under a virtual display.

This smoke proves that critical controls can be activated outside Avalonia's process, the Managed
Recovery Browser becomes externally observable, and the application-owned sensitive confirmation
can be cancelled and confirmed. It does not duplicate the broad in-process matrix and does not
replace the manual NVDA/Orca acceptance boundary.

## Real compositor and real endpoint gates

Xvfb remains the deterministic Linux PR baseline. Before a supported Linux release, run the
published `golden`, both interruption scenarios, and a native browser close/reopen scenario on a
supported physical/VM desktop with `XDG_SESSION_TYPE=wayland` and record OS version, compositor,
WebKit backend, scenario result, and browser cleanup result in the release record. A release is
blocked without explicit evidence; XWayland fallback must be recorded rather than described as a
native Wayland result. This manual gate remains preferable to a misleading virtual-compositor job
until a stable representative hosted runner exists.

The separate `samples/import/real-endpoint-smoke-sample.csv` is exercised only by the explicit
non-destructive procedure in `samples/import/SCENARIOS.md`. Normal PR/release automation remains
loopback-only and never contacts those providers.

## Release confidence matrix

| Boundary | Required evidence |
| --- | --- |
| Platform-neutral domain/security behavior | Blocking unit, integration, SecurityRegression, coverage, analyzer, audit, native-boundary, and CodeQL gates |
| Broad visible workflow | Existing golden/fast/platform/extended desktop matrix |
| Windows/Linux native browser | Blocking Windows, Ubuntu, and Debian desktop scenarios |
| Published artifact startup/browser | Scheduled/manual self-contained `win-x64` and `linux-x64` release-candidate smoke from a clean install root |
| Abrupt restart/orphan cleanup | Both bounded `interruption-*` release scenarios |
| Actual OS-facing control activation | External AT-SPI `external-black-box` release smoke |
| Multi-account UI transitions | Blocking Ubuntu plus Windows/Linux release `multi-account-transition` |
| Real categorized provider destinations | Recorded manual six-row real-endpoint fixture smoke; never authenticated or mutating |
| Native accessibility bridge | Recorded Windows/NVDA and Ubuntu/Orca manual checklist |
| Compositor-sensitive Linux browser | Recorded physical/VM Wayland release run, including whether XWayland was used |
| Live provider locations | Separate scheduled read-only provider-location smoke checks |

Every supported release needs an explicit pass/fail/not-applicable record for every row. A green unit
count, coverage percentage, Xvfb result, or browser navigation observation cannot substitute for a
missing boundary and never constitutes recovery truth.
