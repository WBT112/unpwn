# Application Shell and UI Foundation

The Avalonia desktop application uses an MVVM-oriented shell as its composition root. The shell owns navigation, localization, and presentation state only; recovery logic, storage, cryptography, provider workflows, imports, and exports remain in their dedicated modules and application use cases.

## Shell behavior

The application starts with no vault unlocked. The header displays explicit locked-vault and no-session labels, the vault-entry route is selected, and no recovery data is loaded. A global lock action is visible whenever an injected shell-context service reports an unlocked vault. Locking returns navigation to the vault-entry route.

The stable top-level routes are:

- vault entry
- recovery dashboard
- accounts
- workflow execution
- credentials and export
- completion
- CSV import

Route identifiers remain stable and language-neutral. Their visible labels are resource keys resolved through the localization service.

Navigation exposes workflow prerequisites instead of allowing an operation to fail later with an
unrelated conflict: only vault entry is available while locked, the dashboard becomes available after
unlock, account entry and CSV import require a persisted recovery session, and downstream recovery,
credential, and completion routes require at least one persisted account. Disabled routes remain
visible so the sequence is understandable. Screens refresh their service-backed projection whenever
they are activated, in addition to reacting to change notifications, so data imported on another route
is visible immediately.

Routes whose functional application use cases are not implemented show an explicit localized placeholder instead of simulating recovery behavior.

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

Normal, warning, blocked, failed, successful, and unresolved-risk states use a combination of color, a localized textual state label, and a distinct symbol. Meaning does not depend on color alone. Shared focus styles provide a visible three-pixel focus border, and navigation and action controls use a predictable tab order.

Accessibility names, descriptions, keyboard hints, and dialog consequences are localized. Stable automation IDs and test selectors remain language-neutral so tests and assistive integrations do not depend on visible text.

The documented minimum desktop window size is 760 by 560 logical pixels on Windows and Ubuntu. Content scrolls inside the shell at that size while global context, navigation, and status remain available.

Controls must tolerate expanded pseudo-localized text and longer translations. Layout must not rely on English string length. The shell preserves a path for right-to-left flow direction even though an RTL language is not required for the initial release.

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
