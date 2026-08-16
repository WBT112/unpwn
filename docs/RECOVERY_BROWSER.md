# Recovery Browser Security Boundary

## Purpose

The Recovery Browser is an unpwn-controlled embedded provider work surface. It uses Avalonia `NativeWebView`, backed by WebView2 on Windows and WPE WebKit on Linux.

It is not a general-purpose browser and it is not a source of canonical recovery truth. Its job is to keep the provider website, current recovery guidance, explicit checklist, and credential handoff in one coherent workspace while preserving the existing recovery state machine.

## Truth and dependency boundary

The platform-neutral `RecoveryBrowserSecurityBoundary` consumes an already validated `RecoveryNavigationHandoff` and accepts only destinations within its exact reviewed origin set. Browser-host implementation details remain outside `Unpwn.Core`.

`Unpwn.App` owns the embedded host, platform hardening, localized browser chrome, temporary session lifecycle, and presentation-only credential assistance.

Navigation, redirects, popups, downloads, permissions, TLS events, browser close, field insertion, and provider form state update only transient browser/presentation state. They cannot complete an action, acknowledge a criterion, confirm that a credential works, change risk, or advance the wizard. Only explicit canonical recovery and credential-lifecycle operations may change those states.

## Guided workspace

For a reviewed navigable action, the normal guided path presents the provider page and assistant panel together. The assistant remains the place where the user confirms completion criteria, chooses **Done**, or records that work cannot continue.

The recovery overview exposes one primary **Start recovery** transaction for the recommended account.
That application action loads or creates the canonical execution, starts the current canonical action,
validates its reviewed navigation handoff, and requests the isolated account-bound browser workspace.
It must either make that workspace visible or leave an explicit safe error and external-browser
fallback; navigation alone is not represented as a successful start. Actions without a reviewed
destination remain explicit manual guidance and never cause unpwn to guess a provider page.

The external operating-system browser is an explicitly labelled fallback. Embedded-host failure must never silently downgrade to the external browser.

Each checklist checkmark is persisted through the canonical encrypted account-execution state before the UI reports it as recorded. No provider DOM, screenshot, page text, response body, cookie, or URL is stored as proof. Browser close/restart therefore preserves only explicit unpwn state, not inferred provider success.

## Navigation policy

Production provider content requires absolute HTTPS destinations without embedded user information. Every top-level navigation/redirect must remain within the exact expected-origin set. Subdomains are not implicitly trusted.

The host blocks file, data, JavaScript, custom, and external-application schemes. Popups/new windows are denied rather than silently converted into same-tab navigation. Downloads and website permissions are denied by default. TLS errors and client-certificate requests are not overridden.

An explicit `SyntheticTest` content mode permits HTTP only on loopback for deterministic local test pages. This test boundary must not weaken production requests.

Visible browser chrome shows the normalized origin rather than a full path/query/fragment so reset tokens or credential-bearing URLs are not exposed unnecessarily.

## Profile and session isolation

The Recovery Browser never points at or imports a normal Chrome, Edge, Firefox, or other user profile. Each session uses an opaque unpwn-owned profile path below the local Recovery Browser data root. Profile/session identifiers contain no provider, account name, email address, URL, or credential.

`RecoveryBrowserSessionLifecycle` owns one active browser profile at a time:

- the account association exists only in process memory;
- the same recovery account may reuse its profile across multiple actions;
- a different account cannot inherit that authenticated state;
- account switching remains blocked until cleanup succeeds;
- no authenticated provider session is automatically reconstructed after application restart.

Browser cookies/session storage are temporary operational state, not Recovery Vault records and not canonical recovery evidence.

The profile path is treated as sensitive application-owned storage. On Linux, unpwn creates and verifies the `recovery-browser`, `profiles`, per-session profile, and WPE `data`/`cache` directories with owner-only mode `0700`. The unpwn-owned `.unpwn-session` marker and temporary marker replacement files are created with owner-only mode `0600`. Existing unpwn-owned browser directories and marker files are tightened before reuse or orphan processing. Redirected/symlinked storage, or a failure to establish the required mode, fails closed instead of continuing with a broader filesystem boundary. The surrounding operating-system application-data root is not treated as Recovery Browser-owned storage and is not recursively chmodded.

## Clean close and abnormal termination

A controlled close runs conservatively:

1. explicit unpwn confirmations are already persisted through canonical services;
2. materialized credential presentation and owned clipboard state are cleared;
3. the browser session is marked cleanup-pending without storing account data;
4. the platform engine is asked to clear browsing data;
5. navigation/native browser resources are stopped and released;
6. the complete dedicated profile directory is deleted;
7. cleanup is reported successful only when the owned profile no longer remains.

Resource-release or deletion failure is visible and retryable. The implementation must not race profile deletion against a live browser adapter.

At startup, opaque profile directories left from an abnormal termination are treated as orphaned. New embedded sessions remain blocked until cleanup succeeds. Because account association is not persisted, stale authenticated sessions are discarded rather than automatically resumed. Unexpected entries, symlinks/reparse points, unreadable profile storage, or storage that cannot be restricted to the required Linux owner-only mode fail closed.

Cleanup is not a forensic-erasure guarantee. Filesystem snapshots, backups, storage-device behavior, or an operating-system/browser crash may retain data beyond the application's direct control.

## Generated credential handoff

Password-change/reset actions may attach only a `GeneratedCredentialReference` to canonical execution state. The Recovery Browser resolves that exact action reference; it does not choose a credential merely because it is the newest one for an account.

In-context controls reuse the normal credential safeguards:

- deliberate short-lived reveal;
- owned, time-bounded clipboard copy;
- explicit Mark used and Confirm working lifecycle operations;
- clearing materialized reveal state on vault lock and browser close;
- visible clipboard-cleanup failure;
- no plaintext credential in notes, browser-session metadata, URLs, diagnostics, logs, screenshots, traces, accessibility labels, or persisted UI state.

Browser close itself never marks a credential used or working.

## Provider-reviewed insertion

Automatic insertion is optional and narrow. Manual Reveal/Copy is the safe default.

An insertion adapter is available only when a repository-controlled contract matches the exact provider, action, content mode, expected origins, page evidence, and exact field selectors. Generic/unsupported providers never receive automatic password-field discovery.

A reviewed insertion attempt follows this order:

1. obtain fresh visible user authorization;
2. inspect current origin and page evidence **without reading the secret**;
3. stop on MFA, CAPTCHA, email-link handoff, wrong origin, missing/duplicated fields, or changed content;
4. only after a ready inspection, obtain a short-lived `CredentialSecretLease` from the unlocked vault;
5. immediately re-check the exact contract and set only the reviewed new-password/confirmation fields;
6. dispatch normal input/change events for the provider UI;
7. do not submit the form;
8. after successful insertion, the credential may be explicitly recorded as `Used`, never automatically `Confirmed`, and the recovery action remains incomplete until the user confirms its canonical completion criteria.

The repository currently exposes automatic insertion only for the explicit synthetic-test contract. Real-provider and generic workflows therefore remain manual unless a provider/action adapter is separately reviewed and added.

Browser-script results are reduced to stable non-secret codes. Script exception details are not copied into diagnostics because script execution may temporarily contain the credential.

## Platform behavior

### Windows

Avalonia hosts the installed WebView2 runtime using the dedicated unpwn data directory. The adapter disables or denies platform features that would expand the recovery boundary, including password autosave/general autofill where exposed, OS-account SSO, developer tools, default context menus/browser accelerators, permissions, downloads, external schemes, and certificate exceptions.

### Linux

Avalonia hosts WPE WebKit with dedicated data/cache locations and can use the separately hardened ephemeral WebKitGTK fallback where WPE is unavailable. The WPE profile, `data`, and `cache` locations must pass the owner-only `0700` filesystem check before they are handed to WebKit. The adapter disables persistent credential storage/developer tools where exposed and denies permissions, downloads, and TLS exceptions. WebKitGTK keeps website data ephemeral and still requires the unpwn-owned session profile directory to satisfy the same owner-only storage boundary. WPE WebKit does not expose every WebView2 control through the maintained host; the dedicated profile boundary and conservative lifecycle remain mandatory instead of claiming identical platform hardening.

No platform may silently fall back to an unhardened profile or host.

The assistant initially receives focus when the combined workspace opens. Later action refreshes do
not steal focus while the user is interacting with provider content.

## Testing

Tests use synthetic/loopback content only and cover:

- exact-origin and unsafe-scheme behavior;
- visible origin and browser controls;
- popup/download/permission/default-deny behavior;
- opaque profile paths and account isolation;
- Linux `0700` profile/data/cache directories and `0600` session metadata, including tightening existing owned paths and rejecting redirected storage;
- clear → release → delete cleanup ordering;
- cleanup failure/retry, abnormal termination, orphan detection, and no auto-resume;
- checklist persistence with no browser-driven recovery transition;
- explicit external-browser fallback;
- credential reveal/copy/lock/close cleanup;
- provider-reviewed synthetic insertion, late vault retrieval, wrong origin, changed content, MFA/CAPTCHA/email-link stops, and no form submission;
- rejection of generic automatic DOM insertion;
- synthetic-secret artifact scanning.

See [Testing Strategy](TESTING.md), [Generated Credentials](GENERATED_CREDENTIALS.md), and [Threat Model](THREAT_MODEL.md) for their respective canonical rules.
