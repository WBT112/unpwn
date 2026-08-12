# Recovery Browser Security Boundary

## Purpose

The Recovery Browser is an unpwn-controlled embedded provider work surface. It uses Avalonia
`NativeWebView`, backed by WebView2 on Windows and WPE WebKit on Linux. It is not a general browser,
a custom engine, a Playwright replacement, or a source of canonical recovery truth.

Issue #92 established the host and its fail-closed security boundary. Issue #93 adds account-bound
reuse, cleanup, orphan detection, and crash recovery. The normal guided-workflow integration belongs
to Issue #94, and credential handoff belongs to Issue #95.

## Dependency and truth boundary

The platform-neutral `RecoveryBrowserSecurityBoundary` in `Unpwn.Application` consumes the existing
`RecoveryNavigationHandoff`. It accepts only destinations whose origin appears in the handoff's exact
reviewed origin set. It contains no Avalonia, WebView2, WebKit, vault, or recovery-transition code.

`Unpwn.App` owns:

- the `IRecoveryBrowserHost` presentation contract;
- the `AvaloniaRecoveryBrowserHost` adapter around `NativeWebView`;
- platform hardening adapters for WebView2 and WPE WebKit;
- localized Recovery Browser chrome and visible security events.

Navigation-started, navigation-completed, redirect, popup, download, permission, TLS, and close events
update only transient browser presentation state. They cannot complete an action, acknowledge a
criterion, change risk, or advance the wizard. Only the existing explicit canonical recovery
transitions may do that.

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

1. mark cleanup pending without storing account data;
2. ask the platform engine to clear all browsing data while it is still alive;
3. stop navigation, detach the native view, and wait for the adapter to release profile resources;
4. recursively delete the complete dedicated profile, including cache and browser-created temporary
   or download files;
5. report success only when the directory no longer exists.

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
is no workflow-approved download/export path yet. The existing explicit external-navigation path
remains the fallback until the later integration issue.

## Testing

Application tests exercise exact-origin handling, unsafe schemes, user-information rejection,
loopback-only synthetic HTTP, invalid handoffs, and default-denied capabilities. Avalonia headless
tests render a synthetic provider URI inside `NativeWebView`, verify the visible origin and required
controls, block unexpected origins and popups, project denied platform capabilities, and validate the
opaque profile root. Lifecycle tests cover same-account reuse, cross-account blocking, cleanup
ordering, engine-clear failure with authoritative directory deletion, resource-release failure,
cancellation, delete retry, crash/orphan discovery, unexpected root entries, startup non-resume,
recursive file cleanup, shell warning/retry, and opaque marker content. Tests do not contact or mutate
live providers.
