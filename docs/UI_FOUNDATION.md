# Application Shell and UI Foundation

The Avalonia desktop application uses an MVVM-oriented shell as its composition
root. The shell owns navigation and presentation state only; recovery logic,
storage, cryptography, provider workflows, imports, and exports remain in their
dedicated modules and application use cases.

## Shell behavior

The application starts with no vault unlocked. The header displays explicit
locked-vault and no-session labels, the vault-entry route is selected, and no
recovery data is loaded. A global lock action is visible whenever an injected
shell-context service reports an unlocked vault. Locking returns navigation to
the vault-entry route.

The stable top-level routes are:

- vault entry
- recovery dashboard
- accounts
- workflow execution
- credentials and export
- completion
- CSV import

Routes whose functional application use cases are not implemented show an
explicit placeholder instead of simulating recovery behavior.

## Presentation and accessibility

Normal, warning, blocked, failed, successful, and unresolved-risk states use a
combination of color, a textual state label, and a distinct symbol. Meaning does
not depend on color alone. Shared focus styles provide a visible three-pixel
focus border, and navigation and action controls use a predictable tab order.

The documented minimum desktop window size is 760 by 560 logical pixels on
Windows and Ubuntu. Content scrolls inside the shell at that size while global
context, navigation, and status remain available.

## Commands and confirmations

`AsyncCommand` is the reusable UI command boundary. It prevents repeated
execution, reports busy and cancellation state, accepts a cancellation token,
and converts failures to a caller-provided static message. Source exception
messages are never exposed to the UI.

Sensitive confirmations receive a structured request containing the exact
action, affected item, consequence, and confirm-button label. The reusable
dialog renders every field before allowing confirmation. Functional destructive
or vault operations remain outside the shell issue.

View models and application-facing UI services use constructor injection. The
Avalonia `App` class wires the current locked context, screen factory,
confirmation-dialog service, and shell view model without adding a runtime
service-locator dependency.
