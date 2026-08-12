# Recovery Browser Security Boundary

## Purpose

The Recovery Browser is an unpwn-controlled embedded provider work surface. It uses Avalonia
`NativeWebView`, backed by WebView2 on Windows and WPE WebKit on Linux. It is not a general browser,
a custom engine, a Playwright replacement, or a source of canonical recovery truth.

Issue #92 establishes only the host and its fail-closed security boundary. Account-bound reuse,
cleanup, orphan detection, and crash recovery belong to Issue #93. The normal guided-workflow
integration belongs to Issue #94, and credential handoff belongs to Issue #95.

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
status. Creation, account scoping, reuse, deletion, failure retry, and crash/orphan behavior are
specified and implemented by Issue #93.

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
boundary and the Issue #93 lifecycle remain mandatory rather than claiming equivalent controls.

No silent fallback to an unhardened WebView or a separate ordinary browser profile is allowed. The
existing explicit external-navigation path remains the fallback until the later integration issue.

## Testing

Application tests exercise exact-origin handling, unsafe schemes, user-information rejection,
loopback-only synthetic HTTP, invalid handoffs, and default-denied capabilities. Avalonia headless
tests render a synthetic provider URI inside `NativeWebView`, verify the visible origin and required
controls, block unexpected origins and popups, project denied platform capabilities, and validate the
opaque profile root. Tests do not contact or mutate live providers.
