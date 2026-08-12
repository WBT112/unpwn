# Recovery Browser Security Boundary

## Purpose

The Recovery Browser is an unpwn-controlled embedded provider work surface. It uses Avalonia
`NativeWebView`, backed by WebView2 on Windows and WPE WebKit on Linux. It is not a general browser,
a custom engine, a Playwright replacement, or a source of canonical recovery truth.

Issue #92 established the host and its fail-closed security boundary. Issue #93 added account-bound
reuse, cleanup, orphan detection, and crash recovery. Issue #94 integrates that lifecycle into the
guided recovery workspace. Issue #95 adds in-context generated-credential handoff and a bounded,
repository-reviewed assistance contract without changing the recovery truth boundary.

## Dependency and truth boundary

The platform-neutral `RecoveryBrowserSecurityBoundary` in `Unpwn.Application` consumes the existing
`RecoveryNavigationHandoff`. It accepts only destinations whose origin appears in the handoff's exact
reviewed origin set. It contains no Avalonia, WebView2, WebKit, vault, or recovery-transition code.

`Unpwn.App` owns:

- the `IRecoveryBrowserHost` presentation contract;
- the `AvaloniaRecoveryBrowserHost` adapter around `NativeWebView`;
- platform hardening adapters for WebView2 and WPE WebKit;
- localized Recovery Browser chrome and visible security events;
- the presentation-only credential handoff and repository-controlled browser-assistance catalog.

Navigation-started, navigation-completed, redirect, popup, download, permission, TLS, close, field
insertion, and form-state events update only transient browser/presentation state. They cannot
complete an action, acknowledge a completion criterion, confirm that a credential works, change risk,
or advance the wizard. Only the existing explicit canonical recovery and credential-lifecycle
operations may do that.

## Guided workspace

For a reviewed navigable action, the normal guided path shows the provider page and the current
assistant panel side by side. The assistant remains the only place where the user can explicitly
confirm repository-controlled completion criteria, choose **Done**, or record that work cannot
continue. The external operating-system browser remains a deliberately labelled fallback; embedded
host failure never causes a silent downgrade.

Each criterion checkmark is a canonical execution transition. It stores only the stable criterion
resource key in the encrypted `account-execution` record and updates the UI only after the atomic
execution/dashboard write succeeds. It stores no provider URL, DOM, page text, screenshot, response,
cookie, or inferred evidence. Checkmarks therefore survive controlled browser close and restart,
while the action itself stays in progress until the separate explicit **Done** confirmation succeeds.

The same account can navigate to a subsequent reviewed action handoff while retaining its isolated
profile. The host replaces its origin boundary only from that new validated handoff. A request for a
different account remains blocked by the session lifecycle until cleanup completes.

## Generated credential handoff

Password-change and password-reset actions continue to generate credentials through the canonical
`IGeneratedCredentialRepository` and attach only a `GeneratedCredentialReference` to the canonical
execution action. When an attached credential exists, the Recovery Browser assistant panel can expose
that same credential in context without creating a parallel credential model.

The in-context presentation follows the existing credential safeguards:

- reveal is deliberate and expires after 15 seconds;
- copy uses the owned clipboard path and expires after 30 seconds;
- the UI can explicitly mark the credential as used and later confirm that it works;
- browser close and vault lock immediately drop any revealed string and request owned-clipboard
  cleanup;
- a clipboard cleanup failure remains visible and instructs the user to clear it manually;
- no plaintext credential enters execution notes, reasons, browser-session metadata, URLs,
  diagnostics, logs, screenshots, traces, accessibility labels, or persisted UI state.

The presentation resolves the credential reference from the current canonical account-execution
record. It never chooses a credential merely because it is the newest record for an account.

Closing the browser does not mark a credential as used or working. A credential becomes `Used` only
through an explicit lifecycle operation. It becomes `Confirmed` only through the existing explicit
confirmation after use.

## Bounded provider-reviewed insertion

Assisted field insertion is an optional layer above manual Reveal/Copy. It is not generic DOM
automation and is unavailable unless a repository-controlled adapter matches the exact provider,
action, content mode, expected origins, and page evidence.

The first shipped adapter is deliberately **synthetic-test only**. No live provider gets automatic
field insertion from Issue #95 merely because its page contains password-like inputs. Generic and
unsupported-provider workflows therefore remain manual Reveal/Copy unless a separate reviewed
provider/action adapter is added later.

A reviewed insertion attempt follows this order:

1. show a fresh visible authorization describing the credential insertion;
2. inspect the current browser origin and exact repository-controlled page contract **without reading
   the credential**;
3. stop for MFA, CAPTCHA, email-link handoff, wrong origin, missing/duplicated fields, or changed page
   structure;
4. only after the inspection is ready, obtain a temporary `CredentialSecretLease` from the unlocked
   vault;
5. immediately re-check the exact selectors and set only the reviewed new-password and confirmation
   fields;
6. dispatch normal input/change events so the provider UI can observe the edit;
7. never press the submit button and never translate insertion into recovery completion;
8. after a successful insertion, explicitly record the credential as `Used` through the canonical
   credential repository. Whether it actually works still requires user/provider verification.

The browser adapter returns only small non-secret result codes. Script exceptions are deliberately
collapsed to non-secret failure results rather than copied into diagnostics because an insertion
script necessarily contains the transient credential while it is executing.

## Navigation policy

Production recovery content requires absolute HTTPS URLs without embedded user information. Every
top-level navigation and redirect must match one exact expected origin. Subdomains are not inherited.
The host blocks file, data, JavaScript, custom, and external-application schemes. An explicit
`SyntheticTest` mode permits HTTP only on loopback so deterministic local provider pages can be
rendered without weakening production requests.

New-window and popup requests are always handled and denied. The host does not convert them into
same-tab navigation because that would silently change the reviewed navigation semantics. Downloads
and website permissions are denied by default. TLS errors and client-certificate requests are not
overridden. A platform that cannot install its required security controls is reported unavailable and
navigation is stopped.

The visible chrome shows only the normalized origin, never a path, query, fragment, reset token, or
credential-bearing URL. Browser security events use stable codes mapped to localized UI resources.

## Profile boundary

Every host request receives an opaque profile path below:

```text
<LocalApplicationData>/unpwn/recovery-browser/profiles/<opaque-id>
```

The host rejects paths outside that root. It never points at or imports Chrome, Edge, Firefox, or
another ordinary browser profile. Profile identifiers contain no provider, account, email address, or
other user data. The path is temporary operational browser state, not a vault record or recovery
status.

## Session lifecycle

`RecoveryBrowserSessionLifecycle` owns one active browser profile at a time. The account identifier
is retained only in process memory. The on-disk directory and marker contain an independent opaque
session identifier plus the static lifecycle state `active` or `cleanup-pending`; they contain no
account, provider, email address, URL, cookie, credential, or recovery-state value.

The same account may reuse its active profile across multiple recovery actions. A different account
cannot start until the active session has been cleaned successfully. Suspension is deliberately not
persisted: an application restart never reconstructs the account association and never resumes an
authenticated provider session automatically.

An explicit clean close runs in this order:

1. persist already explicit unpwn confirmations through their canonical services;
2. clear any materialized in-context credential presentation and owned clipboard state;
3. mark browser cleanup pending without storing account data;
4. ask the platform engine to clear all browsing data while it is still alive;
5. stop navigation, detach the native view, and wait for the adapter to release profile resources;
6. recursively delete the complete dedicated profile, including cache and browser-created temporary
   or download files;
7. report success only when the directory no longer exists.

WebView2 uses its profile browsing-data API. WPE WebKit uses its website-data manager to clear all
website-data types. Directory deletion is still authoritative: if the engine-level clear fails but
release and deletion succeed, no profile residue remains. If resource release or deletion fails, the
failure stays visible and retryable; deletion is never raced against a live adapter.

At startup, every opaque directory left below the profile root is classified as orphaned. The shell
shows an assertive warning and offers explicit discard/retry. New browser sessions remain blocked
until the orphan is removed. Unexpected entries, symlinks, reparse points, and unreadable storage fail
closed instead of being ignored or followed. Because no account association is persisted, the only
restart policy is discard and re-authenticate; automatic resume is unavailable.

Closing, crashing, cleanup, or restarting changes no action, completion criterion, risk, plan, or
wizard state. Explicit recovery confirmations must already have been committed through the canonical
encrypted services before a caller ends the operational browser session.

## Platform behavior

### Windows

Avalonia hosts the installed WebView2 runtime. Before creation, unpwn supplies the dedicated user-data
folder and Recovery profile name, disables OS-primary-account SSO and developer tools, and avoids the
ordinary Edge profile. After the adapter is created, unpwn disables password autosave, general
autofill, browser accelerator keys, and default context menus. It subscribes directly to WebView2
permission, download, external-scheme, server-certificate, and client-certificate events and denies
them.

### Linux

Avalonia hosts WPE WebKit using its offscreen embedded control. The system needs the WPE WebKit,
libwpe, and WPE backend runtime libraries documented by Avalonia. unpwn supplies dedicated data and
cache directories, disables developer tools and persistent credential storage, and attaches native
handlers that deny permissions, cancel downloads, and reject TLS-error exceptions. WPE WebKit does
not expose every WebView2 form-autofill toggle through the maintained host; the dedicated profile
boundary and lifecycle remain mandatory rather than claiming equivalent controls.

No silent fallback to an unhardened WebView or a separate ordinary browser profile is allowed.
Downloads remain blocked; any browser-created file is covered by recursive profile cleanup, and there
is no workflow-approved download/export path yet. The existing external-navigation safety path is
available only through the explicit fallback control.

## Testing

Application tests exercise exact-origin handling, unsafe schemes, user-information rejection,
loopback-only synthetic HTTP, invalid handoffs, and default-denied capabilities. Avalonia headless
tests render a synthetic provider URI inside `NativeWebView`, verify the visible origin and required
controls, block unexpected origins and popups, project denied platform capabilities, and validate the
opaque profile root. Lifecycle tests cover same-account reuse, cross-account blocking, cleanup
ordering, engine-clear failure with authoritative directory deletion, resource-release failure,
cancellation, delete retry, crash/orphan discovery, unexpected root entries, startup non-resume,
recursive file cleanup, shell warning/retry, and opaque marker content. Tests do not contact or mutate
live providers. Guided-workspace tests additionally cover reviewed handoff projection, explicit
fallback, persistence failure before visual acknowledgement, close/reload without completion,
same-account reviewed navigation, and the absence of browser-driven recovery transitions.

Credential-assistance headless tests exercise the synthetic reviewed contract, exact current-origin
checking, changed/missing page evidence, MFA/CAPTCHA/email-link stop markers, and successful field
insertion without form submission. Existing credential-presentation tests cover bounded reveal,
clipboard cleanup/failure, and vault-lock clearing; the Recovery Browser reuses those same canonical
credential/clipboard services and additionally clears its materialized presentation on browser close.
The CI artifact scan remains responsible for rejecting the repository's synthetic secret markers from
generated test artifacts.
