# Application Shell and UI Foundation

The Avalonia desktop application uses an MVVM-oriented shell as its composition root. The shell owns navigation, localization, and presentation state only; recovery logic, storage, cryptography, provider workflows, imports, and exports remain in their dedicated modules and application use cases.

## Shell behavior

The application starts with no vault unlocked. The header displays explicit locked-vault and no-session labels, the vault-entry route is selected, and no recovery data is loaded. A global lock action is visible whenever an injected shell-context service reports an unlocked vault. Locking returns navigation to the vault-entry route.

During an active recovery session, the primary shell surface is the guided assistant task card. It
shows the persisted wizard phase, the next task, why it matters, an optional recommended account and
reviewed action, and one context-sensitive primary action. Back and pause remain secondary actions.
When the canonical recommendation changes, keyboard and screen-reader focus moves to the new primary
task action. Merely opening a route never advances the wizard.

The stable top-level routes remain available as secondary detail and correction views behind the
workspace disclosure:

- vault entry
- recovery dashboard
- CSV import
- accounts
- workflow execution
- credentials and export
- completion

Route identifiers remain stable and language-neutral. Their visible labels are resource keys resolved through the localization service.

Navigation exposes workflow prerequisites instead of allowing an operation to fail later with an
unrelated conflict: only vault entry is available while locked, the dashboard becomes available after
unlock, account entry and CSV import require a persisted recovery session, and downstream recovery,
credential, and completion routes require at least one persisted account. Disabled routes remain
visible so the sequence is understandable. Paused sessions keep the dashboard and explicit resume
action available while mutation-capable detail routes remain disabled. Screens refresh their service-backed projection whenever
they are activated, in addition to reacting to change notifications, so data imported on another route
is visible immediately.

Routes whose functional application use cases are not implemented show an explicit localized placeholder instead of simulating recovery behavior.

The shell also owns two global, non-domain status surfaces. Encrypted workspace writes publish visible
saving, saved, retrying, cancelled, and save-failed states; failure text distinguishes access,
storage, version, and lock/conflict cases without exposing source exception details. A prior abnormal
exit produces a dismissible warning that instructs the user to review the recovered state. Neither
surface changes workflow state or implies that an external provider action succeeded.

The workflow-execution route is functional. It resolves the selected or currently recommended account
from the encrypted inventory, binds it to a reviewed repository workflow, and projects the persisted
account-execution aggregate. Unsupported providers fail closed to manual guidance rather than guessed
URLs or actions. Material outcomes return to the dashboard after its recommendation has been
recalculated.

## Localization service

The shell receives one application-wide localization service through constructor injection.

The service owns:

- selected UI culture
- English source-resource fallback
- resource lookup by stable key
- parameterized and plural-sensitive message formatting
- culture-change notifications
- safe missing-key behavior

Views and view models request user-facing text through this boundary. They do not read ambient process culture or hard-code labels, warnings, validation messages, tooltips, accessibility names, or dialog content.

Changing the language refreshes visible presentation state without recreating domain entities, changing route identifiers, unlocking the vault, or rewriting persisted recovery state. The language setting is available before vault unlock.

See [Localization and Multilingual GUI](LOCALIZATION.md).

## Presentation and accessibility

Normal, warning, blocked, failed, successful, and unresolved-risk states use theme-aware chrome, a
localized textual state label, and a distinct symbol. Meaning does not depend on color. Shared focus
styles use operating-system theme brushes and provide a visible three-pixel focus border; navigation
and action controls use a predictable tab order.

Accessibility names, descriptions, keyboard hints, and dialog consequences are localized. Stable automation IDs and test selectors remain language-neutral so tests and assistive integrations do not depend on visible text.

Navigated screens focus their first relevant enabled control. When a validation summary becomes
visible, focus moves to that summary; sensitive confirmations start on the safe cancel action and
restore the invoking focus after closing. Status, persistence, blocking, validation, credential
reveal, and clipboard-expiry surfaces expose appropriate polite or assertive live regions. Timed
secret reveal controls state their 15-second duration, permit immediate hiding, and can be invoked
again after expiry without trapping focus.

The documented minimum desktop window size is 760 by 560 logical pixels on Windows and Ubuntu. Content scrolls inside the shell at that size while global context, navigation, and status remain available.

Controls must tolerate expanded pseudo-localized text and longer translations. Layout must not rely on English string length. The shell preserves a path for right-to-left flow direction even though an RTL language is not required for the initial release.

The automated and manual acceptance contract, including current Avalonia platform limits, is defined
in [Desktop Accessibility Acceptance](ACCESSIBILITY_ACCEPTANCE.md).

## Commands and confirmations

`AsyncCommand` is the reusable UI command boundary. It prevents repeated execution, reports busy and cancellation state, accepts a cancellation token, and converts failures to a stable presentation error code. The view model maps that code to a localized static message. Source exception messages are never exposed to the UI.

Sensitive confirmations receive a structured request containing:

- stable action and consequence codes
- affected-item presentation data
- resource keys for title, explanation, and confirm-button label
- typed formatting arguments

The reusable dialog resolves and renders every field before allowing confirmation. Functional destructive or vault operations remain outside the shell issue.

Localized confirmation text is never used to identify the requested operation. The command executes only from the structured canonical action.

## Culture-sensitive formatting

Dates, times, numbers, and percentages shown in the shell use the explicitly selected UI culture. GUIDs, URLs, provider origins, workflow versions, serialized values, and security-sensitive parsing remain invariant or use their explicitly defined format.

Formatting failures may report the resource key and selected culture to secret-safe diagnostics, but never formatting argument values or source exception messages.

## View-model and code-behind boundary

View models and application-facing UI services use constructor injection. The Avalonia `App` class wires the current locked context, screen factory, localization service, confirmation-dialog service, and shell view model without adding a runtime service-locator dependency.

Code-behind remains limited to view initialization, native file-picker bridging, dialog close results, and Avalonia-specific focus or flow-direction hooks that cannot reasonably live in view models. It must not contain recovery logic, resource selection rules, culture fallback logic, or translated strings.

External provider navigation is exposed through an injected presentation adapter. The view model first
validates the repository-defined handoff and the UI displays the destination and expected origins.
Launcher success, browser return, and elapsed time never call an execution completion transition.

The Recovery Browser foundation provides a reusable embedded view with localized Back, Forward,
Reload, Stop, Close, visible-origin, and security-status chrome. It accepts only a validated
`RecoveryNavigationHandoff` and uses an unpwn-owned profile location. Account-bound sessions may be
reused only for the same account. Closing shows cleanup progress; a failure remains visible and can be
retried. Startup presents orphaned session data as an assertive warning with explicit discard/retry,
never as a resumed provider login. It is not yet the normal guided workflow path: Issue #94 must use
this lifecycle when integrating the assistant and preserve the existing external-navigation fallback.

The vault-entry screen exposes local diagnostics independently of vault unlock. Export requires a
fresh preview of the exact sanitized JSON, an explicit approval checkbox, and a user-selected local
destination. Preview creation does not write or upload anything. A successful export consumes the
preview approval; a failed or cancelled export remains retryable without claiming success.
