# Data Model

## Goals

The data model must support:

- recovery sessions that last days or weeks
- many accounts per session
- service-specific workflows
- simple account categories and service-specific recovery actions
- encrypted generated credentials
- reliable progress reporting
- an audit history without storing secrets in audit events
- presentation in multiple languages without rewriting canonical or persisted data

## Language-neutral data principle

Domain and persisted data remain independent of the selected GUI language.

Canonical data uses:

- stable identifiers
- enum and status values
- structured error and diagnostic codes
- timestamps and numeric values
- workflow and action types
- opaque vault record identifiers

Localized labels, warnings, descriptions, dates, numbers, percentages, and plural-sensitive sentences are produced only in the presentation layer.

Do not persist localized status names, resource output, or localized error messages as canonical data. Changing the selected language must not require a domain migration, workflow migration, audit rewrite, or vault rewrite.

User-authored notes remain exactly as entered and are not machine-translated.

See [Localization and Multilingual GUI](LOCALIZATION.md).

## Core Entities

### RecoverySession

Represents one recovery effort after a suspected incident.

Suggested fields:

- `Id`
- `Name`
- `Status`
- `CreatedAt`
- `UpdatedAt`
- `CompletedAt`
- `SecurityWarningAcknowledgedAt`

Session status values:

- `ACTIVE`
- `PAUSED`
- `COMPLETED`
- `ARCHIVED`

Session names are user-authored content. Status values remain canonical and are mapped to localized presentation resources.

### Account

Represents one user account or digital identity to review.

Suggested fields:

- `Id`
- `RecoverySessionId`
- `ProviderId`
- `DisplayName`
- `LoginIdentifier`
- `AccountUrl`
- `SuggestedCategory`
- `ClassificationCatalogVersion`
- `ConfirmedCategory`
- `CategoryConfirmedRevision`
- `Status`
- `CreatedAt`
- `UpdatedAt`

Account recovery category values:

- `EMAIL`
- `CRITICAL`
- `NON_CRITICAL`
- `UNKNOWN`

Account status values:

- `OPEN`
- `IN_PROGRESS`
- `BLOCKED`
- `FULLY_REVIEWED`
- `REVIEWED_WITH_UNRESOLVED_RISK`
- `ACCESS_LOST`

Provider IDs, category values, catalog versions, confirmation revisions, and statuses are language-neutral. Display names and login identifiers are user data and are never treated as translation keys. The explicit category wins over the persisted local suggestion; explicit `UNKNOWN` is distinct from an unreviewed account.

### Account classification catalog

The repository-controlled classification catalog proposes an account category from stable provider identifiers and safe URL host names. It is versioned, deterministic, local-only, and separate from provider workflow definitions. Unknown services stay `UNKNOWN`, and catalog observations never become recovery truth.

The inventory accepts only its current category schema. Unsupported serialized members fail closed at the inventory persistence boundary rather than being interpreted as recovery state.

Category ordering and provider workflow selection are separate: category answers **when**, workflow answers **how**. The category queue is always `EMAIL`, `CRITICAL`, `UNKNOWN`, then `NON_CRITICAL`, with provider and opaque account IDs as stable tie-breakers. Recovery execution continues to own blocked actions, failed actions, lost access, and unresolved-risk state.

### RecoveryWorkflowDefinition

A versioned provider-defined workflow template.

Suggested fields:

- `WorkflowId`
- `ProviderId`
- `ProviderName`
- `WorkflowVersion`
- `SupportedAccountType`
- `VerifiedAt`
- `RecoveryLocations`
- `Actions`
- display-resource keys where user-facing guidance is required

Definitions are repository-controlled and shipped with an application release. Provider validation and runtime action instances use the same canonical types from `Unpwn.Core`.

Workflow semantics do not contain localized control values. Translation-only changes do not alter workflow versions, paths, prerequisites, or verification dates.

### RecoveryActionDefinition

Describes one action in a workflow template.

Suggested fields:

- `Id`
- `Type`
- `Requirement`
- `Importance`
- `RecoveryPaths`
- `AutomationSupport`
- `Prerequisites`
- `CompletionCriteria`
- resource keys for user-facing title, description, warning, and completion guidance

Importance values and their progress weights are:

- `CRITICAL`: `5`
- `IMPORTANT`: `3`
- `ROUTINE`: `1`

Resource keys are presentation references. They are not used to compare actions or determine completion.

### RecoveryActionInstance

The mutable state of an action for one account.

Suggested fields:

- `Id`
- `AccountId`
- `DefinitionId`
- `Status`
- `StartedAt`
- `CompletedAt`
- `StatusReason`
- `NotApplicableDisposition`
- `HasUnresolvedRisk`
- `UserNotes`

The containing `AccountRecoveryExecutionState` owns the automatically selected recovery path,
structured selection reason, and previous path attempts. Confirmed authenticated access prefers an
authenticated change; otherwise the selector tries password reset and then manual recovery. Failed
or lost-access attempts retain their structured reason and do not disappear when a safe fallback is
materialized. Browser observations are not stored as path-selection input.

Status values:

- `OPEN`
- `IN_PROGRESS`
- `BLOCKED`
- `NEEDS_USER_ACTION`
- `COMPLETED`
- `FAILED`
- `NOT_APPLICABLE`

`NOT_APPLICABLE` always requires a reason and one explicit disposition:

- `TRULY_NOT_APPLICABLE`: the capability is absent for the account type and the action is excluded from required progress
- `UNRESOLVED_RISK`: the control is relevant but unavailable or declined; the action remains in required progress and prevents a fully secured result

Status and disposition values are canonical. User-authored reasons and notes are encrypted content and are never automatically translated.

### CredentialEntry

Stores a newly generated credential during recovery.

Suggested fields:

- `Id`
- `AccountId`
- `EncryptedSecret`
- `GeneratedAt`
- `UsedAt`
- `ConfirmedAt`
- `ExportedAt`
- `DeletedAt`

Old credentials are never stored.

Credential state remains canonical. Localized UI resources describe the state without embedding translated labels in the vault.

### RecoveryProgress

Reports recovery status without implying that unresolved risks are secured. It includes:

- critical accounts secured versus total critical accounts
- overall accounts fully reviewed versus total accounts
- weighted required-action completion using action importance
- blocked required-action count
- failed required-action count
- unresolved-risk count

Critical-account readiness is calculated separately from the overall percentage so blocked critical accounts and accepted unresolved risks remain visible.

The domain returns numbers and structured states. The presentation layer formats counts, dates, percentages, and plural-sensitive messages using the selected UI culture.

### AuditEvent

Records meaningful recovery-state changes without containing user-controlled free text.

Implemented structured fields:

- `OccurredAt`
- `EventType`
- `AccountId` when relevant
- `ActionType` when relevant

Examples:

- account imported
- account category confirmed or changed
- action started
- action completed
- unresolved risk accepted
- credential exported
- vault locked

Human notes and detailed reasons belong to encrypted domain records, not audit event summaries. Audit events must never contain passwords, vault keys, reset tokens, MFA secrets, recovery codes, account notes, browser content, or localized summary text.

The UI maps `EventType` and optional structured fields to localized descriptions at display time. Historical events therefore follow the current selected UI language without modifying the stored audit history.

## Progress Model

A single percentage can create false confidence. unpwn therefore reports several related indicators.

### Critical account readiness

Display:

- number of critical accounts fully reviewed
- total number of critical accounts
- critical accounts that remain blocked, failed, or carry unresolved risk

This is the primary emergency indicator.

### Account coverage

Formula:

```text
fully reviewed accounts / all included accounts
```

Accounts reviewed with unresolved risk are shown separately and do not count as fully reviewed.

### Weighted action progress

Required applicable actions receive fixed weights:

- critical action: `5`
- important action: `3`
- routine action: `1`

Formula:

```text
sum(weights of completed required actions)
/
sum(weights of all applicable required actions)
```

Only actions marked `NOT_APPLICABLE` with a reason and the `TRULY_NOT_APPLICABLE` disposition are excluded from the denominator.

Blocked, failed, and unresolved required actions remain in the denominator.

Optional actions may be displayed separately but do not affect the required-action percentage.

### Blocked and unresolved work

The UI must always show:

- blocked actions
- failed actions
- accounts with unresolved risks
- accounts for which access could not be restored

A high action-progress percentage must not hide these conditions. Missing localized resources must fall back to complete English text rather than suppressing a warning.

## Completion

A recovery session can be marked completed only through an explicit user action.

Before completion, unpwn summarizes:

- critical accounts not fully reviewed
- required actions not completed
- unresolved risks
- credentials not exported or deliberately deleted
- plaintext export files that may still require cleanup
- exported credentials whose password-manager import has not been confirmed

The user may complete a session with unresolved risks, but the final report must preserve those risks and must not describe the session as fully secured.

Completion reports may be rendered in the selected supported language, but canonical status codes, timestamps, numeric values, and machine-readable export fields remain stable.

The persisted `RecoveryCompletionRecord` contains the terminal outcome, completion timestamp, explicit unresolved-risk acknowledgement, and a secret-free `RecoveryCompletionReport`. It never contains account labels, login identifiers, account URLs, user notes, credential identifiers, or credential secret material. Terminal recovery-session lifecycle states are `Completed`, `FollowUpRequired`, and `Archived`; all are read-only unless a separate explicit follow-up workflow is created.
