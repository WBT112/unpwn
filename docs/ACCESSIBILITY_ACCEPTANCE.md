# Desktop Accessibility Acceptance

This document is the release checklist for the Avalonia desktop UI. It complements the automated
presentation tests; it does not claim formal accessibility certification.

## Automated baseline

Pull-request tests must keep the following contracts green:

- `AccessibilityHeadlessTests` verifies focus after navigation, focus on a newly visible validation
  summary, the safe initial confirmation-dialog action, and the status live-region contract.
- `VaultEntryScreenViewModelTests` covers trusted-device gating, vault create/open failures, password
  reveal expiry, and diagnostic-export approval using synthetic data.
- `DashboardScreenViewModelTests`, `AccountInventoryScreenViewModelTests`, and
  `CsvImportScreenViewModelTests` cover session creation, account entry, import review, validation,
  and state refresh.
- `WorkflowExecutionScreenViewModelTests` covers workflow path/action state changes, blocked and
  failed outcomes, completion confirmation, and vault-lock state clearing.
- `CredentialExportScreenViewModelTests` covers reveal/clipboard timeouts, explicit export selection,
  plaintext warnings, handoff confirmation, cleanup, and cancellation.
- `RecoveryCompletionServiceTests` and the completion cases in `ShellViewModelTests` cover completion
  preflight, unresolved-risk acknowledgement, report export, and archival.
- The remaining `ShellViewModelTests` cover prerequisite-disabled routes, navigation refresh, and
  global lock.

Headless tests use synthetic labels and account data only. They do not produce screenshots, native
accessibility-tree dumps, clipboard artifacts, or secret-bearing output.

## Manual release gate

Before an MVP release candidate is published, execute this checklist once on current Ubuntu with
Orca and once on supported Windows with NVDA. Record the date, application commit, OS version,
screen-reader version, tester, and result in the release notes. Any failed critical item blocks the
release.

Start with a fresh synthetic vault and the repository fixtures under `samples/import/`. Never use a
real account, credential, recovery link, token, cookie, MFA secret, or recovery code.

### Keyboard and focus

- Launch without a mouse. Reach language selection, vault entry, and every enabled navigation route
  using Tab, Shift+Tab, arrow keys, Space, and Enter.
- Confirm that unavailable routes are both dimmed and reported as disabled until their documented
  vault/session/account prerequisites are met.
- Traverse vault creation/unlock, session creation, account entry, CSV review/import, recovery
  workflow, credential export, completion preflight, and lock.
- Confirm that each newly opened screen focuses its first relevant action or input.
- Trigger a validation failure on each data-entry screen. Focus must move to the visible validation
  summary without trapping the user.
- Open and cancel every sensitive confirmation with Escape. The safe Cancel action must receive
  initial focus; after closing, focus must return to the invoking control.
- Lock the vault from a downstream route. Focus must return to the vault-unlock input and no
  previously materialized account or credential data may remain exposed.

### Announcements and non-color meaning

- Verify that saving, retrying, locking, blocked, failed, successful, completion, and unresolved-risk
  changes are announced without moving focus unexpectedly.
- Verify that each warning or state includes text and a distinct symbol; turn on the operating-system
  high-contrast theme and confirm that state meaning remains understandable without its color.
- Reveal a vault password and generated credential. The control must announce the 15-second limit,
  allow immediate hiding, expire without a focus trap, and allow the user to reveal it again.
- Copy a synthetic generated credential. The clipboard-clear countdown must be announced and remain
  visible until cleared.

### Scaling, scrolling, and artifact safety

- At 760 by 560 logical pixels, verify that every critical warning, consequence, and primary action
  is reachable by scrolling.
- At 200% OS text scaling, repeat the critical path and verify that text is not truncated in a way
  that changes security meaning.
- Switch to pseudo-localization and repeat navigation, confirmations, CSV review, and completion.
- Inspect logs and any deliberately retained test artifact for `UNPWN_TEST_SECRET_` markers. Do not
  retain screenshots after a secret has been revealed or copied.

## Known Avalonia verification limits

Avalonia headless tests exercise bindings, the control tree, focus, live-region properties, and
dialog behavior, but they do not exercise the Windows UI Automation or Linux AT-SPI bridge itself.
Native file pickers are owned by the operating system and are outside the headless control tree.
Font metrics, high-contrast resource selection, screen-reader phrasing, and platform keyboard
conventions can also differ. The Windows/NVDA and Ubuntu/Orca release run above is therefore required
and cannot be replaced by a green CI result.

The manual run is a release-specific record rather than repository source. A green checklist means
the tested build met this baseline; it is not a claim of formal WCAG certification or of safety for
any account or device.
