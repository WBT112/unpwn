# Data Model

## Purpose and ownership

The platform-neutral model in `Unpwn.Core` is the canonical source for recovery identity, ordering, execution, progress, completion, and audit semantics. Presentation models, dashboard rows, browser state, and localized text are projections or transient context; they must not create a second recovery state machine.

This document maps the implemented aggregates and their ownership. Detailed transition rules live in the linked feature documents instead of being repeated here.

## Language-neutral data

Canonical and persisted data uses stable identifiers, enums, structured reason/error codes, revisions, timestamps, numeric counters, workflow/action types, URLs, and opaque GUIDs. User-visible labels, warnings, descriptions, dates, percentages, and plural-sensitive sentences are produced at the presentation localization boundary.

Do not persist translated status names, resource output, localized errors, or browser-rendered text as recovery state. A language change must not require a domain, workflow, audit, or vault migration. User-authored notes and reasons remain exactly as entered and are never machine-translated.

See [Localization](LOCALIZATION.md).

## Canonical aggregates

| Aggregate | Owns | Does not own |
| --- | --- | --- |
| `RecoveryWizardState` | current/safe-resume step, trusted-device decision, lifecycle, revision | session content, account ordering, browser observations |
| `RecoverySessionWorkspace` | session identity/name, retained incident guidance, lifecycle, account dashboard projection, terminal completion record | account identity details, action execution, credential plaintext |
| `AccountInventoryState` | account identity metadata, catalog suggestion/version, explicit user category, inventory revision | provider workflow or action outcomes |
| `AccountRecoveryExecutionState` | selected provider workflow/path, access state, prior path attempts, action state, completion acknowledgements, generated-credential references | account labels, credential secrets, browser content |
| generated-credential record and `GeneratedCredentialMetadata` | generated secret while retained, lifecycle, opaque references, secret-free audit entries | old credentials or provider/browser state |
| `RecoveryCompletionRecord` | explicit terminal outcome and secret-free final report | account labels, URLs, notes, credential identifiers or secrets |

### Integrated flow state

`RecoveryWizardState` contains an opaque ID, current step, conservative resume step, lifecycle, trusted-device decision, vault-context flag, revision, and timestamp. Supported step IDs are defined by `RecoveryWizardStepId`; unknown serialized steps fail closed.

The wizard coordinates services but does not own their state. `RecoveryNextUserTask` combines the latest wizard, session, inventory, execution, and credential projections into one language-neutral next task. Navigation is an output of that projection and never a recovery input.

See [Integrated Recovery Flow](RECOVERY_WIZARD.md).

### Recovery session

`RecoverySessionWorkspace` contains:

- opaque session ID and editable local display name;
- the retained structured incident input;
- `Active`, `Paused`, `Archived`, `Completed`, or `FollowUpRequired` lifecycle;
- created/updated timestamps and monotonically increasing revision;
- language-neutral `RecoveryAccountDashboardEntry` projections;
- an optional terminal `RecoveryCompletionRecord`.

Terminal workspaces are read-only. The session projection is not the source for account metadata or action transitions and can be rebuilt from those canonical aggregates.

See [Recovery Session and Overview](RECOVERY_SESSION_DASHBOARD.md).

### Account inventory and category

`AccountInventoryState` is scoped to one session and owns its revision, update timestamp, and `AccountInventoryEntry` values. Each entry contains:

- opaque account ID;
- provider ID;
- optional account name, login identifier, and safe HTTP/HTTPS account URL;
- repository catalog suggestion and catalog version;
- optional explicit user category plus the revision at which it was confirmed;
- update timestamp.

Recovery categories are `Email`, `Critical`, `Unknown`, and `NonCritical`. `Unknown` is a system-only unresolved suggestion and cannot be stored as an explicit user confirmation. The user's explicit category wins over the catalog suggestion.

`AccountRecoveryOrder` is derived deterministically as `Email → Critical → Unknown → NonCritical`, followed by stable provider and opaque account-ID tie-breakers. It does not own execution outcomes.

See [Account Inventory and Recovery Queue](ACCOUNT_INVENTORY.md).

### Provider workflow definitions

`RecoveryWorkflowDefinition` is immutable repository metadata containing provider/workflow identity, version, account type, verification date, reviewed recovery locations/origins, and action definitions.

`RecoveryActionDefinition` contains stable action identity/type, requirement, importance, supported recovery paths, prerequisites, completion-criterion resource keys, optional reviewed location, and automation support. Resource keys are presentation references, never control values.

Definitions are shipped with the application and validated before use. They are not downloaded as runtime plugins.

See [Recovery Workflows](RECOVERY_WORKFLOWS.md).

### Account recovery execution

`AccountRecoveryExecutionState` is bound to one inventory account and one exact workflow/version. It owns:

- provider/workflow identity and automatically selected recovery path/reason;
- confirmed access state and encrypted non-secret reason;
- previous path attempts and fallback reasons;
- created/updated timestamps and revision;
- `RecoveryActionExecutionState` values for the selected path.

Each action state stores its canonical status, structured reason, optional encrypted user reason/notes, timestamps, unresolved-risk/not-applicable disposition, optional opaque generated-credential reference, and acknowledged repository completion-criterion keys.

Action statuses are `Open`, `InProgress`, `Blocked`, `NeedsUserAction`, `Completed`, `Failed`, and `NotApplicable`. A required action cannot be silently skipped. `NotApplicable` requires either:

- `TrulyNotApplicable`, which excludes an absent capability from required progress; or
- `UnresolvedRisk`, which keeps the relevant control unresolved.

Browser URLs, DOM/page content, cookies, reset links, credential values, and translated text are never execution state.

See [Account Recovery Execution](ACCOUNT_RECOVERY_EXECUTION.md).

### Generated credentials

unpwn stores only newly generated temporary credentials, never old passwords. Canonical execution references a credential with opaque credential/account IDs; plaintext access requires an unlocked vault and a short-lived disposable lease.

`GeneratedCredentialMetadata` tracks generation, use, confirmation, export, password-manager handoff, plaintext cleanup, deletion, revision, and structured secret-free audit events. File creation, password-manager confirmation, and plaintext cleanup are separate states.

See [Generated Credentials](GENERATED_CREDENTIALS.md).

### Completion

`RecoveryCompletionPreflight` is revision-bound and contains structured issues, not a mutable terminal state. It must be rebuilt when the session, inventory, execution, or credential metadata changes.

`RecoveryCompletionRecord` stores:

- `Completed`, `FollowUpRequired`, or `Archived` outcome;
- completion timestamp;
- explicit unresolved-risk acceptance where required;
- a secret-free `RecoveryCompletionReport`.

The report contains opaque session/account IDs, provider/action IDs, canonical issue codes, timestamps, and aggregate counters. It excludes account labels, login identifiers, URLs, notes, credential identifiers, and secrets.

## Projections and progress

`RecoveryDashboardSnapshot`, account dashboard entries, queue recommendations, `RecoveryNextUserTask`, and view models are derived projections. They may explain state but cannot mutate canonical truth by being opened, refreshed, or navigated.

Progress deliberately separates:

- critical accounts ready versus total critical accounts;
- fully reviewed accounts versus all accounts;
- weighted required-action completion;
- blocked and failed required actions;
- lost access and unresolved risks;
- credential handoff and plaintext-cleanup work.

A high percentage never hides blockers or means an account/device is secure. Deferred accounts remain open and appear in completion preflight.

## Encrypted persistence map

| Vault record type | Identifier scope | Canonical payload |
| --- | --- | --- |
| `recovery-session` | fixed opaque IDs | wizard state and recovery-session workspace in separate records |
| `account-state` | fixed opaque inventory ID | `AccountInventoryState` |
| `account-execution` | opaque account ID | `AccountRecoveryExecutionState` |
| `generated-credential` | opaque credential ID | generated secret plus lifecycle metadata while retained |

Record type, opaque identifier, and schema version are authenticated associated data. Unsupported schema members, invalid enums/IDs/revisions, workflow-version mismatch, or malformed aggregate structure fail closed rather than being silently reinterpreted.

Logically related session/wizard, inventory/dashboard, and execution/dashboard changes use the documented atomic batch boundary. Materialized state is published only after successful encrypted persistence.

Browser cookies, profile data, page content, and navigation history are temporary operational state outside the Recovery Vault. They never become canonical recovery evidence.

See [Workspace Persistence](WORKSPACE_PERSISTENCE.md), [Vault Security](VAULT_SECURITY.md), and [Recovery Browser Security Boundary](RECOVERY_BROWSER.md).

## Audit boundary

Generated-credential audit entries contain an opaque operation ID, repository-defined event type,
and timestamp. They may not contain passwords, keys, reset tokens, MFA secrets, recovery codes,
browser content, account notes, source exception text, or localized summaries.

The UI maps structured event types to the selected language at display time, so changing language does not rewrite history.
